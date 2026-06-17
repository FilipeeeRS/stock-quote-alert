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
        // 1) Argumentos de linha de comando.
        if (!CliArguments.TryParse(args, out var cli, out var error))
        {
            Console.Error.WriteLine($"Erro: {error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliArguments.UsageText);
            return 1;
        }

        // 2) Configuração (caminho via env var STOCK_ALERT_CONFIG ou 'config.json' padrão).
        string configPath = Environment.GetEnvironmentVariable("STOCK_ALERT_CONFIG")
                            ?? Path.Combine(AppContext.BaseDirectory, "config.json");

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

        // 3) Composição das dependências.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        IQuoteProvider quotes = new BrapiQuoteProvider(http, settings.Api);
        INotifier notifier = new EmailNotifier(settings.Smtp, settings.AlertRecipient);

        var monitor = new StockMonitor(quotes, notifier, cli!, settings);

        // 4) Encerramento gracioso via Ctrl+C.
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Encerrando...");
            cts.Cancel();
        };

        try
        {
            await monitor.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // saída normal por Ctrl+C
        }

        return 0;
    }
}
