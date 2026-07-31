namespace StockQuoteAlert.Configuration;

/// <summary>
/// Raiz do arquivo de configuração (config.json).
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Destinatário fixo, usado só pelo modo da Etapa 1
    /// (stock-quote-alert PETR4 22.67 22.59). No modo de inscrições os
    /// destinatários vêm do banco, então este campo não é necessário.
    /// </summary>
    public string AlertRecipient { get; set; } = string.Empty;

    public SmtpSettings Smtp { get; set; } = new();
    public ApiSettings Api { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public MonitoringSettings Monitoring { get; set; } = new();

    /// <summary>Intervalo entre consultas de cotação, em segundos (modo Etapa 1).</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Tempo mínimo (em minutos) entre dois alertas do mesmo tipo,
    /// para evitar enxurrada de e-mails enquanto o preço oscila na faixa.
    /// </summary>
    public int AlertCooldownMinutes { get; set; } = 15;

    /// <summary>
    /// Endereço da página que recebe o clique no botão "Cancelar" do e-mail.
    /// Fica vazio até o site existir (Etapa 4); enquanto isso o e-mail mostra
    /// o comando de cancelamento em vez de um link.
    /// </summary>
    public string CancelBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Valida campos obrigatórios e lança caso algo esteja faltando.
    /// </summary>
    /// <param name="requireRecipient">
    /// O modo da Etapa 1 exige 'alertRecipient'; o modo de inscrições, não.
    /// </param>
    public void Validate(bool requireRecipient = true)
    {
        var errors = new List<string>();

        if (requireRecipient && string.IsNullOrWhiteSpace(AlertRecipient))
            errors.Add("'alertRecipient' não pode ser vazio.");

        if (string.IsNullOrWhiteSpace(Smtp.Host))
            errors.Add("'smtp.host' não pode ser vazio.");

        if (Smtp.Port <= 0)
            errors.Add("'smtp.port' deve ser maior que zero.");

        if (string.IsNullOrWhiteSpace(Smtp.FromAddress))
            errors.Add("'smtp.fromAddress' não pode ser vazio.");

        if (PollIntervalSeconds <= 0)
            errors.Add("'pollIntervalSeconds' deve ser maior que zero.");

        if (Monitoring.TickIntervalMinutes <= 0)
            errors.Add("'monitoring.tickIntervalMinutes' deve ser maior que zero.");

        if (Monitoring.BuyPercentile < 0 || Monitoring.SellPercentile > 100 ||
            Monitoring.BuyPercentile >= Monitoring.SellPercentile)
            errors.Add("'monitoring.buyPercentile' deve ser menor que 'sellPercentile', " +
                       "e ambos entre 0 e 100.");

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

public sealed class DatabaseSettings
{
    /// <summary>Caminho do arquivo SQLite. Relativo à pasta de execução.</summary>
    public string Path { get; set; } = "data/alerts.db";
}

public sealed class MonitoringSettings
{
    /// <summary>
    /// De quantos em quantos minutos o worker faz uma rodada.
    ///
    /// Cinco minutos não é chute: no plano gratuito da brapi a cotação chega com
    /// cerca de 30 minutos de atraso, então consultar mais rápido não traz
    /// informação nova — só gasta cota. Com 5 minutos, e consultando apenas
    /// durante o pregão, cabem uns 8 ativos distintos nas 15.000 chamadas/mês.
    /// </summary>
    public int TickIntervalMinutes { get; set; } = 5;

    /// <summary>Se true, o worker dorme fora do pregão da B3 em vez de gastar cota.</summary>
    public bool RespectMarketHours { get; set; } = true;

    /// <summary>Janela de histórico pedida à API para calcular os limites.</summary>
    public string HistoryRange { get; set; } = "6mo";

    /// <summary>Abaixo deste percentil do histórico, o ativo é considerado barato.</summary>
    public int BuyPercentile { get; set; } = 20;

    /// <summary>Acima deste percentil do histórico, o ativo é considerado caro.</summary>
    public int SellPercentile { get; set; } = 80;

    /// <summary>
    /// De quantas em quantas horas refazer o cálculo dos limites. O histórico
    /// é diário, então recalcular a cada rodada não mudaria nada.
    /// </summary>
    public int ThresholdRefreshHours { get; set; } = 24;

    /// <summary>
    /// Pausa entre consultas de ativos diferentes, em milissegundos.
    /// A brapi limita as chamadas por minuto; espaçar evita levar bloqueio.
    /// </summary>
    public int DelayBetweenTickersMs { get; set; } = 3500;
}
