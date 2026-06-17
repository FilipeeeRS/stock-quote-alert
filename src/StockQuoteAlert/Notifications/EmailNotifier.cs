using System.Globalization;
using System.Net;
using System.Net.Mail;
using StockQuoteAlert.Alerts;
using StockQuoteAlert.Configuration;

namespace StockQuoteAlert.Notifications;

/// <summary>
/// Envia alertas por e-mail usando SMTP (System.Net.Mail.SmtpClient).
/// </summary>
public sealed class EmailNotifier : INotifier
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly SmtpSettings _smtp;
    private readonly string _recipient;

    public EmailNotifier(SmtpSettings smtp, string recipient)
    {
        _smtp = smtp;
        _recipient = recipient;
    }

    public async Task NotifyAsync(AlertType type, string ticker, decimal currentPrice,
        decimal threshold, CancellationToken cancellationToken)
    {
        (string subject, string body) = BuildMessage(type, ticker, currentPrice, threshold);

        using var message = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(_recipient);

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(_smtp.Username))
            client.Credentials = new NetworkCredential(_smtp.Username, _smtp.Password);

        await client.SendMailAsync(message, cancellationToken);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] E-mail de {Action(type)} enviado para {_recipient}.");
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

        string body =
            $"""
            Alerta automático de cotação — {ticker}

            O preço atual ({priceStr}) {reason}.
            Sugestão: {action.ToUpper(PtBr)}.

            Horário: {DateTime.Now:dd/MM/yyyy HH:mm:ss}

            --
            Este é um aviso automático. Não constitui recomendação de investimento.
            """;

        return (subject, body);
    }

    private static string Action(AlertType type) => type == AlertType.Sell ? "venda" : "compra";
}
