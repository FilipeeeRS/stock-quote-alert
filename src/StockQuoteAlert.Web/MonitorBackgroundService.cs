using StockQuoteAlert.Configuration;
using StockQuoteAlert.Monitoring;

namespace StockQuoteAlert.Web;

/// <summary>
/// Roda o worker dentro do mesmo processo do site.
///
/// POR QUE JUNTOS: as hospedagens gratuitas dão um processo só. Separar em dois
/// serviços custaria dinheiro sem trazer benefício nesta escala.
///
/// E continua sendo o mesmo "tique" da Etapa 2, sem estado na memória — então,
/// se um dia for melhor separar, ou deixar um agendador externo chamar, nada
/// aqui precisa mudar.
/// </summary>
public sealed class MonitorBackgroundService : BackgroundService
{
    private readonly MonitorTick _tick;
    private readonly MonitoringSettings _settings;
    private readonly ILogger<MonitorBackgroundService> _logger;

    public MonitorBackgroundService(MonitorTick tick, AppSettings settings,
        ILogger<MonitorBackgroundService> logger)
    {
        _tick = tick;
        _settings = settings.Monitoring;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_settings.TickIntervalMinutes);

        _logger.LogInformation(
            "Worker iniciado: uma rodada a cada {Minutos} min, histórico {Janela}, percentis {Compra}/{Venda}.",
            interval.TotalMinutes, _settings.HistoryRange,
            _settings.BuyPercentile, _settings.SellPercentile);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_settings.RespectMarketHours && !MarketHours.IsOpen(DateTime.UtcNow))
                {
                    _logger.LogDebug("Fora do pregão da B3 — pulando esta rodada.");
                }
                else
                {
                    TickResult result = await _tick.RunOnceAsync(stoppingToken);

                    if (result.TickersChecked > 0 || result.NoticesSent > 0)
                        _logger.LogInformation(
                            "Rodada: {Ativos} ativo(s), {Inscricoes} inscrição(ões), {Enviados} e-mail(s).",
                            result.TickersChecked, result.SubscriptionsEvaluated, result.NoticesSent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // desligando o site
            }
            catch (Exception ex)
            {
                // Rede de proteção final: nada derruba o worker nem o site junto.
                _logger.LogError(ex, "Erro inesperado na rodada do worker.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
