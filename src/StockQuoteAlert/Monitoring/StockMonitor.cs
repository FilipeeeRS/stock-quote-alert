using StockQuoteAlert.Alerts;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Configuration;
using StockQuoteAlert.Notifications;
using StockQuoteAlert.Quotes;

namespace StockQuoteAlert.Monitoring;

/// <summary>
/// Monitora continuamente a cotação e dispara alertas quando o preço cruza os limites.
/// </summary>
public sealed class StockMonitor
{
    private readonly IQuoteProvider _quotes;
    private readonly INotifier _notifier;
    private readonly CliArguments _args;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _cooldown;
    private readonly Func<DateTime> _clock;

    // Estado para cooldown e detecção de cruzamento (evita reenvio a cada tick na mesma faixa).
    private DateTime? _lastSellAlert;
    private DateTime? _lastBuyAlert;
    private AlertType? _lastZone;

    public StockMonitor(IQuoteProvider quotes, INotifier notifier,
        CliArguments args, AppSettings settings, Func<DateTime>? clock = null)
    {
        _quotes = quotes;
        _notifier = notifier;
        _args = args;
        _interval = TimeSpan.FromSeconds(settings.PollIntervalSeconds);
        _cooldown = TimeSpan.FromMinutes(settings.AlertCooldownMinutes);
        _clock = clock ?? (() => DateTime.UtcNow); // injetável para testes
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Monitorando {_args.Ticker} | venda > {_args.SellThreshold} | " +
                          $"compra < {_args.BuyThreshold} | intervalo {_interval.TotalSeconds:0}s");
        Console.WriteLine("Pressione Ctrl+C para encerrar.");
        Console.WriteLine(new string('-', 60));

        while (!cancellationToken.IsCancellationRequested)
        {
            decimal? price = await _quotes.GetCurrentPriceAsync(_args.Ticker, cancellationToken);
            if (price is not null)
            {
                // O relógio interno é UTC; na tela mostramos horário de Brasília.
                Console.WriteLine($"[{Stamp()}] {_args.Ticker} = {price.Value:0.00}");
                await ProcessPriceAsync(price.Value, cancellationToken);
            }

            try
            {
                await Task.Delay(_interval, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Avalia um preço e dispara o alerta apropriado se necessário.
    /// Visível internamente para testes determinísticos.
    /// </summary>
    internal async Task ProcessPriceAsync(decimal value, CancellationToken cancellationToken)
    {
        if (value > _args.SellThreshold)
            await MaybeAlert(AlertType.Sell, value, _args.SellThreshold, cancellationToken);
        else if (value < _args.BuyThreshold)
            await MaybeAlert(AlertType.Buy, value, _args.BuyThreshold, cancellationToken);
        else
            _lastZone = null; // faixa neutra; permite novo alerta ao próximo cruzamento
    }

    private async Task MaybeAlert(AlertType type, decimal price, decimal threshold,
        CancellationToken cancellationToken)
    {
        // Só alerta de novo se: mudou de zona (novo cruzamento) OU passou o cooldown.
        bool newCrossing = _lastZone != type;
        bool cooledDown = HasCooledDown(type);

        if (!newCrossing && !cooledDown)
            return;

        try
        {
            await _notifier.NotifyAsync(type, _args.Ticker, price, threshold, cancellationToken);
            RecordAlert(type);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Stamp()}] Falha ao enviar alerta: {ex.Message}");
        }
    }

    private bool HasCooledDown(AlertType type)
    {
        DateTime? last = type == AlertType.Sell ? _lastSellAlert : _lastBuyAlert;
        return last is null || _clock() - last.Value >= _cooldown;
    }

    private string Stamp() => MarketHours.ToBrasilia(_clock()).ToString("HH:mm:ss");

    private void RecordAlert(AlertType type)
    {
        DateTime now = _clock();
        if (type == AlertType.Sell) _lastSellAlert = now;
        else _lastBuyAlert = now;
        _lastZone = type;
    }
}
