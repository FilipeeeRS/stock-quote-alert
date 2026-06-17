using System.Globalization;

namespace StockQuoteAlert.Cli;

/// <summary>
/// Argumentos posicionais: ticker, preço de venda (linha azul), preço de compra (linha vermelha).
/// </summary>
public sealed record CliArguments(string Ticker, decimal SellThreshold, decimal BuyThreshold)
{
    public static bool TryParse(string[] args, out CliArguments? result, out string? error)
    {
        result = null;
        error = null;

        if (args.Length != 3)
        {
            error = "São esperados exatamente 3 parâmetros.";
            return false;
        }

        string ticker = args[0].Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ticker))
        {
            error = "O ticker do ativo não pode ser vazio.";
            return false;
        }

        if (!TryParseDecimal(args[1], out decimal sell))
        {
            error = $"Preço de venda inválido: '{args[1]}'.";
            return false;
        }

        if (!TryParseDecimal(args[2], out decimal buy))
        {
            error = $"Preço de compra inválido: '{args[2]}'.";
            return false;
        }

        if (sell <= 0 || buy <= 0)
        {
            error = "Os preços de referência devem ser positivos.";
            return false;
        }

        // Sanidade: o preço de venda (linha azul) fica acima do de compra (linha vermelha).
        if (sell <= buy)
        {
            error = $"O preço de venda ({sell}) deve ser maior que o de compra ({buy}).";
            return false;
        }

        result = new CliArguments(ticker, sell, buy);
        return true;
    }

    // Aceita tanto ponto quanto vírgula como separador decimal.
    private static bool TryParseDecimal(string input, out decimal value)
    {
        input = input.Trim().Replace(',', '.');
        return decimal.TryParse(input, NumberStyles.Number,
            CultureInfo.InvariantCulture, out value);
    }

    public static string UsageText =>
        """
        Uso:
          stock-quote-alert <ATIVO> <PRECO_VENDA> <PRECO_COMPRA>

        Exemplo:
          stock-quote-alert PETR4 22.67 22.59

        Onde:
          ATIVO         Ticker do ativo na B3 (ex.: PETR4)
          PRECO_VENDA   Linha azul  — acima deste valor, dispara alerta de VENDA
          PRECO_COMPRA  Linha vermelha — abaixo deste valor, dispara alerta de COMPRA
        """;
}
