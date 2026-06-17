namespace StockQuoteAlert.Configuration;

/// <summary>
/// Raiz do arquivo de configuração (config.json).
/// </summary>
public sealed class AppSettings
{
    public string AlertRecipient { get; set; } = string.Empty;
    public SmtpSettings Smtp { get; set; } = new();
    public ApiSettings Api { get; set; } = new();

    /// <summary>Intervalo entre consultas de cotação, em segundos.</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Tempo mínimo (em minutos) entre dois alertas do mesmo tipo,
    /// para evitar enxurrada de e-mails enquanto o preço oscila na faixa.
    /// </summary>
    public int AlertCooldownMinutes { get; set; } = 15;

    /// <summary>Valida campos obrigatórios e lança caso algo esteja faltando.</summary>
    public void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(AlertRecipient))
            errors.Add("'alertRecipient' não pode ser vazio.");

        if (string.IsNullOrWhiteSpace(Smtp.Host))
            errors.Add("'smtp.host' não pode ser vazio.");

        if (Smtp.Port <= 0)
            errors.Add("'smtp.port' deve ser maior que zero.");

        if (string.IsNullOrWhiteSpace(Smtp.FromAddress))
            errors.Add("'smtp.fromAddress' não pode ser vazio.");

        if (PollIntervalSeconds <= 0)
            errors.Add("'pollIntervalSeconds' deve ser maior que zero.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Configuração inválida:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
    }
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Stock Quote Alert";
}

public sealed class ApiSettings
{
    public string BaseUrl { get; set; } = "https://brapi.dev/api/quote/";

    /// <summary>Token da brapi.dev. Opcional para os ativos de teste (PETR4, VALE3, etc.).</summary>
    public string Token { get; set; } = string.Empty;
}
