namespace StockQuoteAlert.Quotes;

/// <summary>
/// Abstração para uma fonte de cotações. Facilita troca de provedor e testes.
/// </summary>
public interface IQuoteProvider
{
    /// <summary>
    /// Retorna o preço atual do ativo, ou null se a cotação não pôde ser obtida.
    /// </summary>
    Task<decimal?> GetCurrentPriceAsync(string ticker, CancellationToken cancellationToken);
}
