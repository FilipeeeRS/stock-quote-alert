using StockQuoteAlert.Alerts;
using StockQuoteAlert.Notifications;
using StockQuoteAlert.Quotes;

namespace StockQuoteAlert.Tests;

/// <summary>Provedor de cotações fixo (não usado diretamente nos testes de ProcessPrice).</summary>
internal sealed class FakeQuoteProvider : IQuoteProvider
{
    public decimal? Next { get; set; }
    public Task<decimal?> GetCurrentPriceAsync(string ticker, CancellationToken ct)
        => Task.FromResult(Next);
}

/// <summary>Notificador que apenas registra as chamadas, sem enviar e-mail.</summary>
internal sealed class RecordingNotifier : INotifier
{
    public List<(AlertType Type, decimal Price, decimal Threshold)> Sent { get; } = new();

    public Task NotifyAsync(AlertType type, string ticker, decimal currentPrice,
        decimal threshold, CancellationToken ct)
    {
        Sent.Add((type, currentPrice, threshold));
        return Task.CompletedTask;
    }
}
