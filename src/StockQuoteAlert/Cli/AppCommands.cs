using StockQuoteAlert.Alerts;
using StockQuoteAlert.Configuration;
using StockQuoteAlert.Data;
using StockQuoteAlert.Monitoring;
using StockQuoteAlert.Notifications;
using StockQuoteAlert.Quotes;
using StockQuoteAlert.Validation;

namespace StockQuoteAlert.Cli;

/// <summary>
/// Os comandos do modo de inscrições (Etapa 2).
///
/// Estes comandos existem para você conseguir testar o banco e o worker antes
/// de o site existir. Quando chegarmos à Etapa 4, a página vai chamar as mesmas
/// operações — o que muda é só a porta de entrada.
/// </summary>
public static class AppCommands
{
    public static int Subscribe(AppSettings settings, string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Uso: stock-quote-alert inscrever <ATIVO> <email>");
            return 1;
        }

        string ticker = InputValidator.NormalizeTicker(args[1]);
        string email = args[2].Trim();

        if (!InputValidator.IsValidTicker(ticker))
        {
            Console.Error.WriteLine($"Erro: '{args[1]}' não parece um ativo da B3 (ex.: PETR4, TAEE11).");
            return 1;
        }

        if (!InputValidator.IsValidEmail(email))
        {
            Console.Error.WriteLine($"Erro: '{email}' não parece um e-mail válido.");
            return 1;
        }

        var repository = new SubscriptionRepository(OpenDatabase(settings));
        Subscription? created = repository.Add(ticker, email, DateTime.UtcNow);

        if (created is null)
        {
            Console.Error.WriteLine($"Erro: {email} já acompanha {ticker}.");
            return 1;
        }

        Console.WriteLine($"Inscrição criada: {ticker} -> {email}");
        Console.WriteLine($"Token de cancelamento: {created.CancelToken}");
        Console.WriteLine();
        Console.WriteLine("Para cancelar:");
        Console.WriteLine($"  stock-quote-alert cancelar {created.CancelToken}");
        return 0;
    }

    public static int List(AppSettings settings)
    {
        var database = OpenDatabase(settings);
        var subscriptions = new SubscriptionRepository(database).ListAll();
        var assets = new AssetRepository(database);

        if (subscriptions.Count == 0)
        {
            Console.WriteLine("Nenhuma inscrição cadastrada.");
            Console.WriteLine("Crie uma com: stock-quote-alert inscrever PETR4 voce@email.com");
            return 0;
        }

        Console.WriteLine($"{"ID",-4} {"ATIVO",-8} {"E-MAIL",-30} {"SITUAÇÃO",-10} {"FAIXA",-8} TOKEN");
        Console.WriteLine(new string('-', 100));

        foreach (Subscription s in subscriptions)
        {
            string status = s.Active ? "ativa" : "cancelada";
            string zone = s.LastZone?.ToDb() ?? "—";
            Console.WriteLine($"{s.Id,-4} {s.Ticker,-8} {Truncate(s.Email, 30),-30} " +
                              $"{status,-10} {zone,-8} {s.CancelToken}");
        }

        Console.WriteLine();
        Console.WriteLine("Limites calculados por ativo:");

        foreach (string ticker in subscriptions.Select(s => s.Ticker).Distinct().Order())
        {
            Asset? asset = assets.Get(ticker);
            if (asset?.HasUsableThresholds == true)
            {
                Console.WriteLine(
                    $"  {ticker}: preço {asset.CurrentPrice:0.00} | " +
                    $"compra < {asset.BuyThreshold:0.00} | venda > {asset.SellThreshold:0.00}");
            }
            else
            {
                Console.WriteLine($"  {ticker}: ainda não calculado (rode 'monitorar' uma vez)");
            }
        }

        return 0;
    }

    public static int Cancel(AppSettings settings, string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Uso: stock-quote-alert cancelar <token>");
            return 1;
        }

        var repository = new SubscriptionRepository(OpenDatabase(settings));
        string? email = repository.CancelByToken(args[1].Trim(), DateTime.UtcNow);

        if (email is null)
        {
            Console.Error.WriteLine("Token não encontrado, ou a inscrição já estava cancelada.");
            return 1;
        }

        Console.WriteLine($"Inscrição de {email} cancelada. Não serão mais enviados avisos.");
        return 0;
    }

    /// <summary>
    /// O worker: repete a rodada de verificação até você apertar Ctrl+C.
    /// </summary>
    public static async Task<int> MonitorAsync(AppSettings settings, bool once,
        CancellationToken cancellationToken)
    {
        var database = OpenDatabase(settings);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        IQuoteProvider quotes = new BrapiQuoteProvider(http, settings.Api);
        var notifier = new EmailNotifier(settings.Smtp, settings.AlertRecipient, settings.CancelBaseUrl);

        var tick = new MonitorTick(
            quotes,
            notifier,
            new SubscriptionRepository(database),
            new AssetRepository(database),
            new NoticeRepository(database),
            settings.Monitoring,
            TimeSpan.FromMinutes(settings.AlertCooldownMinutes));

        var interval = TimeSpan.FromMinutes(settings.Monitoring.TickIntervalMinutes);

        Console.WriteLine($"Banco: {database.FilePath}");
        Console.WriteLine($"Rodada a cada {interval.TotalMinutes:0} min | " +
                          $"histórico {settings.Monitoring.HistoryRange} | " +
                          $"percentis {settings.Monitoring.BuyPercentile}/{settings.Monitoring.SellPercentile} | " +
                          $"cooldown {settings.AlertCooldownMinutes} min");
        Console.WriteLine(once ? "Rodada única." : "Pressione Ctrl+C para encerrar.");
        Console.WriteLine(new string('-', 70));

        while (!cancellationToken.IsCancellationRequested)
        {
            DateTime utcNow = DateTime.UtcNow;

            if (settings.Monitoring.RespectMarketHours && !MarketHours.IsOpen(utcNow) && !once)
            {
                Console.WriteLine($"[{MarketHours.ToBrasilia(utcNow):HH:mm:ss}] " +
                                  "Fora do pregão da B3 — aguardando (não gasta cota da API).");
            }
            else
            {
                try
                {
                    TickResult result = await tick.RunOnceAsync(cancellationToken);
                    Console.WriteLine(
                        $"Rodada: {result.TickersChecked} ativo(s), " +
                        $"{result.SubscriptionsEvaluated} inscrição(ões), " +
                        $"{result.NoticesSent} e-mail(s) enviado(s)" +
                        (result.TickersFailed > 0 ? $", {result.TickersFailed} falha(s)" : "") +
                        (result.SendFailures > 0 ? $", {result.SendFailures} envio(s) com erro" : ""));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Última rede de proteção: nada derruba o worker.
                    Console.Error.WriteLine($"Erro inesperado na rodada: {ex.Message}");
                }
            }

            if (once)
                break;

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        return 0;
    }

    private static Database OpenDatabase(AppSettings settings)
    {
        var database = new Database(settings.Database.Path);
        database.EnsureCreated();
        return database;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
