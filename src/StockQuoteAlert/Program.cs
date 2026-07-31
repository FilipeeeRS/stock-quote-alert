using StockQuoteAlert.Cli;
using StockQuoteAlert.Configuration;
using StockQuoteAlert.Monitoring;
using StockQuoteAlert.Notifications;
using StockQuoteAlert.Quotes;

namespace StockQuoteAlert;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Encerramento gracioso via Ctrl+C, comum a todos os modos.
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Encerrando...");
            cts.Cancel();
        };

        string configPath = Environment.GetEnvironmentVariable("STOCK_ALERT_CONFIG")
                            ?? Path.Combine(AppContext.BaseDirectory, "config.json");

        string command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;

        try
        {
            return command switch
            {
                // ----- Etapa 2: modo de inscrições -----
                // Estes três só mexem no banco, então funcionam mesmo sem
                // config.json — dá para testar o banco antes de configurar o e-mail.
                "inscrever" => AppCommands.Subscribe(ConfigLoader.LoadOrDefault(configPath), args),
                "listar" => AppCommands.List(ConfigLoader.LoadOrDefault(configPath)),
                "cancelar" => AppCommands.Cancel(ConfigLoader.LoadOrDefault(configPath), args),

                // O worker precisa de SMTP e da API, mas não de 'alertRecipient'
                // (os destinatários vêm do banco).
                "monitorar" => await AppCommands.MonitorAsync(
                    ConfigLoader.Load(configPath, requireRecipient: false),
                    once: args.Contains("--uma-vez"),
                    cts.Token),

                "ajuda" or "--help" or "-h" => ShowUsage(),

                // ----- Etapa 1: modo original, preservado -----
                _ => await RunLegacyAsync(args, configPath, cts.Token)
            };
        }
        catch (OperationCanceledException)
        {
            return 0; // saída normal por Ctrl+C
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erro: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// O comportamento da Etapa 1, intacto: monitora um único ativo com os
    /// limites informados na linha de comando e avisa um destinatário fixo.
    /// </summary>
    private static async Task<int> RunLegacyAsync(string[] args, string configPath,
        CancellationToken cancellationToken)
    {
        if (!CliArguments.TryParse(args, out var cli, out var error))
        {
            Console.Error.WriteLine($"Erro: {error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(UsageText);
            return 1;
        }

        AppSettings settings;
        try
        {
            settings = ConfigLoader.Load(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erro de configuração: {ex.Message}");
            return 2;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        IQuoteProvider quotes = new BrapiQuoteProvider(http, settings.Api);
        INotifier notifier = new EmailNotifier(settings.Smtp, settings.AlertRecipient);

        var monitor = new StockMonitor(quotes, notifier, cli!, settings);

        try
        {
            await monitor.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // saída normal por Ctrl+C
        }

        return 0;
    }

    private static int ShowUsage()
    {
        Console.WriteLine(UsageText);
        return 0;
    }

    private static string UsageText =>
        $"""
        Stock Quote Alert

        MODO INSCRIÇÕES (Etapa 2)
          stock-quote-alert inscrever <ATIVO> <email>   Cadastra um aviso
          stock-quote-alert listar                      Mostra inscrições e limites
          stock-quote-alert cancelar <token>            Cancela pelo token do e-mail
          stock-quote-alert monitorar [--uma-vez]       Roda o worker

        Os limites de compra e venda são calculados automaticamente a partir do
        histórico recente do ativo — o usuário não digita preço nenhum.

        MODO ORIGINAL (Etapa 1)
        {CliArguments.UsageText}
        """;
}
