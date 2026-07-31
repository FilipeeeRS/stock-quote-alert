using StockQuoteAlert.Analysis;
using Xunit;

namespace StockQuoteAlert.Tests;

/// <summary>
/// Testes do cálculo que substituiu os preços digitados pelo usuário.
/// Nenhum deles precisa de internet nem do mercado aberto.
/// </summary>
public class ThresholdCalculatorTests
{
    /// <summary>Preços de 1 a 100, para as contas de percentil ficarem previsíveis.</summary>
    private static decimal[] OneToHundred() =>
        Enumerable.Range(1, 100).Select(i => (decimal)i).ToArray();

    [Fact]
    public void Calcula_percentis_com_interpolacao()
    {
        var result = ThresholdCalculator.Compute(OneToHundred(), buyPercentile: 20, sellPercentile: 80);

        Assert.NotNull(result);
        // posição = 0,20 × 99 = 19,8 → entre o 20º (20) e o 21º (21) valor
        Assert.Equal(20.80m, result!.Buy);
        // posição = 0,80 × 99 = 79,2 → entre o 80º (80) e o 81º (81) valor
        Assert.Equal(80.20m, result.Sell);
    }

    [Fact]
    public void Limite_de_venda_sempre_maior_que_o_de_compra()
    {
        // A regra que veio da Etapa 1 precisa continuar valendo.
        var result = ThresholdCalculator.Compute(OneToHundred());

        Assert.NotNull(result);
        Assert.True(result!.Sell > result.Buy);
    }

    [Fact]
    public void Recusa_historico_curto_demais()
    {
        var poucos = Enumerable.Repeat(10m, ThresholdCalculator.MinimumDataPoints - 1).ToArray();

        Assert.Null(ThresholdCalculator.Compute(poucos));
    }

    [Fact]
    public void Recusa_quando_o_preco_nao_variou()
    {
        // Se o ativo ficou parado, os dois limites empatam e todo preço cairia
        // numa zona de alerta. Melhor não dar sinal nenhum.
        var parado = Enumerable.Repeat(25m, 120).ToArray();

        Assert.Null(ThresholdCalculator.Compute(parado));
    }

    [Fact]
    public void Recusa_lista_nula()
    {
        Assert.Null(ThresholdCalculator.Compute(null!));
    }

    [Fact]
    public void Rejeita_percentis_invertidos()
    {
        Assert.Throws<ArgumentException>(() =>
            ThresholdCalculator.Compute(OneToHundred(), buyPercentile: 80, sellPercentile: 20));
    }

    [Fact]
    public void A_janela_acompanha_quando_o_preco_muda_de_patamar()
    {
        // Este é o problema que motivou a mudança: um limite fixo digitado à mão
        // ficaria obsoleto se a ação saltasse de R$ 20 para R$ 120 e ficasse lá.
        var faixaAntiga = Enumerable.Repeat(0, 120).Select((_, i) => 18m + i % 5).ToArray();
        var faixaNova = Enumerable.Repeat(0, 120).Select((_, i) => 118m + i % 5).ToArray();

        var antes = ThresholdCalculator.Compute(faixaAntiga);
        var depois = ThresholdCalculator.Compute(faixaNova);

        Assert.NotNull(antes);
        Assert.NotNull(depois);
        Assert.True(depois!.Sell > antes!.Sell);
        Assert.True(depois.Buy > antes.Buy);
    }
}
