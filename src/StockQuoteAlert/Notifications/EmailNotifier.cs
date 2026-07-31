using System.Globalization;
using System.Net;
using System.Net.Mail;
using StockQuoteAlert.Alerts;
using StockQuoteAlert.Configuration;
using StockQuoteAlert.Monitoring;

namespace StockQuoteAlert.Notifications;

/// <summary>
/// Envia alertas por e-mail usando SMTP (System.Net.Mail.SmtpClient).
///
/// Implementa as duas formas de envio: a da Etapa 1 (um destinatário fixo,
/// vindo do config.json) e a dos inscritos (um e-mail por inscrição, cada um
/// com seu botão de cancelamento).
/// </summary>
public sealed class EmailNotifier : INotifier, ISubscriberNotifier
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly SmtpSettings _smtp;
    private readonly string _recipient;
    private readonly string _cancelBaseUrl;

    public EmailNotifier(SmtpSettings smtp, string recipient, string cancelBaseUrl = "")
    {
        _smtp = smtp;
        _recipient = recipient;
        _cancelBaseUrl = cancelBaseUrl;
    }

    // ----- Etapa 1: destinatário único -----

    public async Task NotifyAsync(AlertType type, string ticker, decimal currentPrice,
        decimal threshold, CancellationToken cancellationToken)
    {
        (string subject, string body) = BuildMessage(type, ticker, currentPrice, threshold);
        await SendAsync(_recipient, subject, body, null, cancellationToken);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] E-mail de {Action(type)} enviado para {_recipient}.");
    }

    // ----- Etapa 2: um e-mail por inscrito -----

    public async Task NotifyAsync(SubscriberAlert alert, CancellationToken cancellationToken)
    {
        (string subject, string body) =
            BuildMessage(alert.Type, alert.Ticker, alert.CurrentPrice, alert.Threshold);

        string? cancelUrl = BuildCancelUrl(alert.CancelToken);
        body += BuildCancelSection(alert.CancelToken, cancelUrl);

        await SendAsync(alert.Email, subject, body, cancelUrl, cancellationToken);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] E-mail de {Action(alert.Type)} " +
                          $"({alert.Ticker}) enviado para {alert.Email}.");
    }

    // ----- infraestrutura comum -----

    private async Task SendAsync(string to, string subject, string body,
        string? cancelUrl, CancellationToken cancellationToken)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(to);

        // Cabeçalho padrão de descadastro: os provedores de e-mail mostram um
        // botão nativo "cancelar inscrição" e passam a confiar mais no remetente,
        // o que ajuda a não cair na caixa de spam.
        if (!string.IsNullOrWhiteSpace(cancelUrl))
            message.Headers.Add("List-Unsubscribe", $"<{cancelUrl}>");

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(_smtp.Username))
            client.Credentials = new NetworkCredential(_smtp.Username, _smtp.Password);

        await client.SendMailAsync(message, cancellationToken);
    }

    private (string subject, string body) BuildMessage(AlertType type, string ticker,
        decimal price, decimal threshold)
    {
        string action = Action(type);
        string priceStr = price.ToString("C", PtBr);
        string thresholdStr = threshold.ToString("C", PtBr);

        string subject = $"[Stock Alert] {ticker}: sugestão de {action} a {priceStr}";

        string reason = type == AlertType.Sell
            ? $"subiu acima do preço de referência de venda ({thresholdStr})"
            : $"caiu abaixo do preço de referência de compra ({thresholdStr})";

        // Horário de Brasília explícito: se um dia isto rodar num servidor
        // fora do Brasil, o e-mail continua mostrando a hora que a pessoa espera.
        DateTime now = MarketHours.ToBrasilia(DateTime.UtcNow);

        string body =
            $"""
            Alerta automático de cotação — {ticker}

            O preço atual ({priceStr}) {reason}.
            Sugestão: {action.ToUpper(PtBr)}.

            Horário: {now:dd/MM/yyyy HH:mm:ss} (horário de Brasília)

            --
            Este é um aviso automático. Não constitui recomendação de investimento.
            Os limites são calculados comparando o preço com os últimos meses do
            próprio ativo; o comportamento passado não garante o futuro.
            """;

        return (subject, body);
    }

    private string? BuildCancelUrl(string token) =>
        string.IsNullOrWhiteSpace(_cancelBaseUrl)
            ? null
            : $"{_cancelBaseUrl.TrimEnd('/')}?token={Uri.EscapeDataString(token)}";

    private static string BuildCancelSection(string token, string? cancelUrl) =>
        cancelUrl is not null
            ? $"""


              Não quer mais receber estes avisos?
              Cancelar: {cancelUrl}
              """
            : $"""


              Não quer mais receber estes avisos?
              Rode: stock-quote-alert cancelar {token}
              (o botão de cancelamento vira um link quando o site estiver no ar)
              """;

    private static string Action(AlertType type) => type == AlertType.Sell ? "venda" : "compra";
}
