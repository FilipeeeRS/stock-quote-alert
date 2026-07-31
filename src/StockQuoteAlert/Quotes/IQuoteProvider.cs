namespace StockQuoteAlert.Quotes;

/// <summary>
/// Preço atual de um ativo junto com o histórico de fechamentos recentes.
/// Os dois vêm na mesma chamada à API, então o histórico não custa cota extra.
/// </summary>
/// <param name="ClosingPrices">
/// Fechamentos diários, do mais antigo para o mais recente. Usamos o fechamento
/// "cru" (e não o ajustado por proventos) para comparar com o preço atual, que
/// também é cru — misturar os dois enviesaria a conta.
/// </param>
public sealed record QuoteSnapshot(
    string Ticker,
    decimal Price,
    IReadOnlyList<decimal> ClosingPrices);

/// <summary>
/// Abstração para uma fonte de cotações. Facilita troca de provedor e testes.
/// </summary>
public interface IQuoteProvider
{
    /// <summary>
    /// Retorna o preço atual do ativo, ou null se a cotação não pôde ser obtida.
    /// </summary>
    Task<decimal?> GetCurrentPriceAsync(string ticker, CancellationToken cancellationToken);

    /// <summary>
    /// Retorna preço atual + histórico, ou null se não pôde ser obtido.
    /// </summary>
    /// <param name="range">Janela do histórico no formato da API (ex.: "6mo", "3mo", "1y").</param>
    Task<QuoteSnapshot?> GetSnapshotAsync(string ticker, string range,
        CancellationToken cancellationToken);
}
