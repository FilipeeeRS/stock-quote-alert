using StockQuoteAlert.Alerts;

namespace StockQuoteAlert.Data;

/// <summary>
/// Uma inscrição: "este e-mail quer ser avisado sobre este ativo".
///
/// Os três últimos campos são o estado do alerta. Na Etapa 1 eles eram
/// variáveis dentro do StockMonitor e sumiam ao fechar o programa; agora
/// moram no banco, então reiniciar o worker não reenvia avisos repetidos.
/// </summary>
public sealed record Subscription(
    long Id,
    string Ticker,
    string Email,
    string CancelToken,
    bool Active,
    DateTime CreatedAt,
    DateTime? CancelledAt,
    Zone? LastZone,
    DateTime? LastBuyNoticeAt,
    DateTime? LastSellNoticeAt);

/// <summary>
/// Um ativo monitorado, com a última cotação lida e os limites calculados
/// a partir do histórico. Compartilhado por todos os inscritos naquele ativo.
/// </summary>
/// <param name="BuyPercentile">
/// Qual percentil gerou o limite guardado. Serve para perceber que a
/// configuração mudou e refazer a conta, em vez de continuar usando um
/// limite calculado com outra regra.
/// </param>
public sealed record Asset(
    string Ticker,
    decimal? CurrentPrice,
    decimal? BuyThreshold,
    decimal? SellThreshold,
    DateTime? ThresholdsComputedAt,
    DateTime? CheckedAt,
    int? BuyPercentile = null,
    int? SellPercentile = null)
{
    /// <summary>
    /// Os limites só servem se os dois existirem e fizerem sentido
    /// (venda acima de compra — a regra que veio da Etapa 1).
    /// </summary>
    public bool HasUsableThresholds =>
        BuyThreshold is not null && SellThreshold is not null &&
        SellThreshold > BuyThreshold;
}
