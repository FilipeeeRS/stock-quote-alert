using StockQuoteAlert.Alerts;
using StockQuoteAlert.Analysis;
using StockQuoteAlert.Configuration;
using StockQuoteAlert.Data;
using StockQuoteAlert.Notifications;
using StockQuoteAlert.Quotes;

namespace StockQuoteAlert.Monitoring;

/// <summary>Resumo de uma rodada, útil para log e para os testes.</summary>
public sealed record TickResult(
    int TickersChecked,
    int TickersFailed,
    int SubscriptionsEvaluated,
    int NoticesSent,
    int SendFailures);

/// <summary>
/// Uma rodada completa de verificação: lê os ativos com inscrição ativa,
/// consulta o preço de cada um UMA vez e aplica a regra de alerta para
/// todos os inscritos daquele ativo.
///
/// POR QUE ISTO É UM "TIQUE" E NÃO UM LOOP ETERNO
/// Este método começa e termina, sem guardar nada na memória entre chamadas —
/// todo o estado mora no banco. Hoje um loop simples chama ele a cada 5 minutos.
/// Mas, quando for publicar, um agendador externo pode chamar exatamente o mesmo
/// método, e isso importa: as hospedagens gratuitas praticamente não deixam mais
/// nada ligado 24 horas por dia.
/// </summary>
public sealed class MonitorTick
{
    private readonly IQuoteProvider _quotes;
    private readonly ISubscriberNotifier _notifier;
    private readonly SubscriptionRepository _subscriptions;
    private readonly AssetRepository _assets;
    private readonly NoticeRepository _notices;
    private readonly MonitoringSettings _settings;
    private readonly TimeSpan _cooldown;
    private readonly Func<DateTime> _clock;

    public MonitorTick(
        IQuoteProvider quotes,
        ISubscriberNotifier notifier,
        SubscriptionRepository subscriptions,
        AssetRepository assets,
        NoticeRepository notices,
        MonitoringSettings settings,
        TimeSpan cooldown,
        Func<DateTime>? clock = null)
    {
        _quotes = quotes;
        _notifier = notifier;
        _subscriptions = subscriptions;
        _assets = assets;
        _notices = notices;
        _settings = settings;
        _cooldown = cooldown;
        _clock = clock ?? (() => DateTime.UtcNow); // injetável para testes
    }

    public async Task<TickResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> tickers = _subscriptions.DistinctActiveTickers();

        if (tickers.Count == 0)
        {
            Console.WriteLine("Nenhuma inscrição ativa. Nada a fazer nesta rodada.");
            return new TickResult(0, 0, 0, 0, 0);
        }

        int checkedCount = 0, failed = 0, evaluated = 0, sent = 0, sendFailures = 0;
        bool first = true;

        foreach (string ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Espaça as chamadas para não esbarrar no limite por minuto da brapi.
            if (!first)
                await DelayAsync(_settings.DelayBetweenTickersMs, cancellationToken);
            first = false;

            try
            {
                var outcome = await ProcessTickerAsync(ticker, cancellationToken);

                checkedCount++;
                evaluated += outcome.Evaluated;
                sent += outcome.Sent;
                sendFailures += outcome.SendFailures;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Regra que veio da Etapa 1: um ativo com problema não pode
                // derrubar o processo nem impedir os outros de serem verificados.
                failed++;
                Console.Error.WriteLine($"[{Stamp()}] Falha ao processar {ticker}: {ex.Message}");
            }
        }

        return new TickResult(checkedCount, failed, evaluated, sent, sendFailures);
    }

    private async Task<(int Evaluated, int Sent, int SendFailures)> ProcessTickerAsync(
        string ticker, CancellationToken cancellationToken)
    {
        QuoteSnapshot? snapshot =
            await _quotes.GetSnapshotAsync(ticker, _settings.HistoryRange, cancellationToken);

        if (snapshot is null)
        {
            // O provedor já explicou o motivo no log. Seguimos para o próximo ativo.
            return (0, 0, 0);
        }

        DateTime now = Now();
        Asset? stored = _assets.Get(ticker);
        Thresholds? thresholds = ResolveThresholds(stored, snapshot, now);

        var asset = new Asset(
            Ticker: ticker,
            CurrentPrice: snapshot.Price,
            BuyThreshold: thresholds?.Buy,
            SellThreshold: thresholds?.Sell,
            ThresholdsComputedAt: thresholds is null
                ? stored?.ThresholdsComputedAt
                : (ReusedThresholds(stored, thresholds) ? stored!.ThresholdsComputedAt : now),
            CheckedAt: now);

        _assets.Save(asset);

        if (thresholds is null)
        {
            Console.WriteLine(
                $"[{Stamp()}] {ticker} = {snapshot.Price:0.00} | sem limites: " +
                $"o histórico tem {snapshot.ClosingPrices.Count} dias " +
                $"(mínimo {ThresholdCalculator.MinimumDataPoints}) ou o preço não variou.");
            return (0, 0, 0);
        }

        Console.WriteLine(
            $"[{Stamp()}] {ticker} = {snapshot.Price:0.00} | " +
            $"compra < {thresholds.Buy:0.00} | venda > {thresholds.Sell:0.00}");

        return await NotifySubscribersAsync(ticker, snapshot.Price, thresholds, now, cancellationToken);
    }

    /// <summary>
    /// Reaproveita os limites já calculados enquanto estiverem recentes.
    /// O histórico é diário, então refazer a conta a cada 5 minutos não mudaria nada.
    /// </summary>
    private Thresholds? ResolveThresholds(Asset? stored, QuoteSnapshot snapshot, DateTime now)
    {
        bool fresh = stored?.ThresholdsComputedAt is not null &&
                     now - stored.ThresholdsComputedAt.Value <
                         TimeSpan.FromHours(_settings.ThresholdRefreshHours);

        if (fresh && stored!.HasUsableThresholds)
            return new Thresholds(stored.BuyThreshold!.Value, stored.SellThreshold!.Value);

        return ThresholdCalculator.Compute(
            snapshot.ClosingPrices, _settings.BuyPercentile, _settings.SellPercentile);
    }

    private static bool ReusedThresholds(Asset? stored, Thresholds thresholds) =>
        stored?.BuyThreshold == thresholds.Buy && stored?.SellThreshold == thresholds.Sell;

    private async Task<(int Evaluated, int Sent, int SendFailures)> NotifySubscribersAsync(
        string ticker, decimal price, Thresholds thresholds, DateTime now,
        CancellationToken cancellationToken)
    {
        var subscriptions = _subscriptions.ListActiveByTicker(ticker);
        int sent = 0, failures = 0;

        foreach (Subscription subscription in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AlertDecision decision = AlertEngine.Evaluate(
                subscription, price, thresholds.Buy, thresholds.Sell, now, _cooldown);

            if (!decision.ShouldNotify)
            {
                // Sem e-mail, mas a faixa pode ter mudado (ex.: entrou na neutra).
                // Guardar isso é o que permite detectar o próximo cruzamento.
                if (subscription.LastZone != decision.Zone)
                    _subscriptions.SaveAlertState(
                        AlertEngine.ApplyZoneOnly(subscription, decision.Zone));
                continue;
            }

            try
            {
                await _notifier.NotifyAsync(
                    new SubscriberAlert(
                        subscription.Email,
                        decision.Type!.Value,
                        ticker,
                        price,
                        decision.Threshold!.Value,
                        subscription.CancelToken),
                    cancellationToken);

                // Só marca como avisado depois que o envio deu certo. Se falhar,
                // o estado fica como estava e a próxima rodada tenta de novo.
                _subscriptions.SaveAlertState(
                    AlertEngine.ApplyNotified(subscription, decision.Zone, now));

                _notices.Add(subscription.Id, decision.Type.Value, price,
                    decision.Threshold.Value, now);

                sent++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine(
                    $"[{Stamp()}] Falha ao enviar para {subscription.Email}: {ex.Message}");
            }
        }

        return (subscriptions.Count, sent, failures);
    }

    private static Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, cancellationToken);

    private DateTime Now() => _clock();

    /// <summary>
    /// Hora para o log. Guardamos tudo em UTC no banco, mas mostrar UTC na tela
    /// confunde: você leria "06:52" à meia-noite. Aqui convertemos para Brasília.
    /// </summary>
    private string Stamp() => MarketHours.ToBrasilia(Now()).ToString("HH:mm:ss");
}
