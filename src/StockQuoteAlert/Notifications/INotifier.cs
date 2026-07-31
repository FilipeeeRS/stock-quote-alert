using StockQuoteAlert.Alerts;

namespace StockQuoteAlert.Notifications;

/// <summary>
/// Abstração para envio de alertas. Permite trocar e-mail por outro canal e facilita testes.
/// Usada pelo modo da Etapa 1, em que existe um único destinatário fixo no config.json.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(AlertType type, string ticker, decimal currentPrice,
        decimal threshold, CancellationToken cancellationToken);
}

/// <summary>Os dados de um aviso para um inscrito.</summary>
/// <param name="CancelToken">
/// O código secreto que o botão "Cancelar" do e-mail carrega. Cada inscrição
/// tem o seu, aleatório, para ninguém conseguir cancelar a inscrição de outra pessoa.
/// </param>
public sealed record SubscriberAlert(
    string Email,
    AlertType Type,
    string Ticker,
    decimal CurrentPrice,
    decimal Threshold,
    string CancelToken);

/// <summary>
/// Envio de alertas para os inscritos (Etapa 2 em diante): cada e-mail vai para
/// um destinatário diferente e leva o próprio botão de cancelamento.
/// </summary>
public interface ISubscriberNotifier
{
    Task NotifyAsync(SubscriberAlert alert, CancellationToken cancellationToken);
}
