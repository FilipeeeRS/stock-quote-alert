namespace StockQuoteAlert.Analysis;

/// <summary>Os dois limites calculados para um ativo.</summary>
public sealed record Thresholds(decimal Buy, decimal Sell);

/// <summary>
/// Calcula, a partir do histórico recente, a partir de que preço o ativo está
/// "barato" e a partir de que preço está "caro".
///
/// POR QUE PERCENTIL, E NÃO MÉDIA
/// Usamos percentis porque o resultado é explicável em uma frase: o limite de
/// compra é o preço abaixo do qual o ativo esteve em apenas 20% dos últimos
/// meses. Dá para escrever isso no e-mail e a pessoa entende. Média com desvio
/// padrão assume que os preços se distribuem em forma de sino, o que muitas
/// vezes não acontece, e um único dia atípico entorta a conta toda.
///
/// POR QUE ISSO RESOLVE O PROBLEMA DO LIMITE FIXO
/// Se a ação sai de R$ 20 e passa o ano em R$ 120, a janela de histórico
/// acompanha: depois de alguns meses, R$ 120 vira o novo normal e o sistema
/// para de avisar. Um número digitado à mão nunca se ajustaria sozinho.
///
/// NÃO É RECOMENDAÇÃO DE INVESTIMENTO. É uma comparação estatística com o
/// passado recente, e o passado não garante o futuro.
/// </summary>
public static class ThresholdCalculator
{
    /// <summary>Mínimo de dias necessário para a conta significar alguma coisa.</summary>
    public const int MinimumDataPoints = 30;

    /// <summary>
    /// Devolve os limites, ou null quando não dá para calcular com confiança:
    /// histórico curto demais, ou preço que praticamente não variou no período
    /// (aí os dois limites ficariam iguais e todo preço cairia numa zona de alerta).
    /// </summary>
    public static Thresholds? Compute(
        IReadOnlyList<decimal> closingPrices,
        int buyPercentile = 20,
        int sellPercentile = 80)
    {
        if (closingPrices is null || closingPrices.Count < MinimumDataPoints)
            return null;

        if (buyPercentile < 0 || sellPercentile > 100 || buyPercentile >= sellPercentile)
            throw new ArgumentException(
                "Percentis inválidos: o de compra deve ser menor que o de venda " +
                "e ambos devem ficar entre 0 e 100.");

        // Ordenar é o coração do percentil: depois de ordenado, "o 20% mais barato"
        // é simplesmente olhar a posição que fica a 20% do caminho da lista.
        decimal[] sorted = closingPrices.OrderBy(p => p).ToArray();

        decimal buy = Percentile(sorted, buyPercentile);
        decimal sell = Percentile(sorted, sellPercentile);

        // A regra que veio da Etapa 1: venda tem de ser maior que compra.
        // Aqui ela é consequência natural (p80 >= p20), mas se o ativo ficou
        // parado no mesmo preço os dois empatam — e aí não há sinal a dar.
        if (sell <= buy)
            return null;

        return new Thresholds(Round(buy), Round(sell));
    }

    /// <summary>
    /// Percentil com interpolação: se a posição calculada cai entre dois dias,
    /// pegamos um valor proporcional entre eles em vez de arredondar para um lado.
    /// </summary>
    private static decimal Percentile(decimal[] sorted, int percentile)
    {
        if (sorted.Length == 1)
            return sorted[0];

        decimal position = (percentile / 100m) * (sorted.Length - 1);

        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);

        if (lower == upper)
            return sorted[lower];

        decimal weight = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
