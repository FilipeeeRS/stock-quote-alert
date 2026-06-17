using StockQuoteAlert.Alerts;

namespace StockQuoteAlert.Notifications;

/// <summary>
/// Abstração para envio de alertas. Permite trocar e-mail por outro canal e facilita testes.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(AlertType type, string ticker, decimal currentPrice,
        decimal threshold, CancellationToken cancellationToken);
}
