using StockQuoteAlert.Alerts;
using StockQuoteAlert.Data;
using StockQuoteAlert.Monitoring;
using Xunit;

namespace StockQuoteAlert.Tests;

/// <summary>
/// A regra de alerta por inscrição. É a mesma da Etapa 1 (cruzamento + cooldown),
/// agora com o estado vindo do banco em vez da memória.
/// </summary>
public class AlertEngineTests
{
    private const decimal Compra = 22.59m;
    private const decimal Venda = 22.67m;

    private static readonly DateTime Agora = new(2026, 7, 29, 14, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    private static Subscription Inscricao(
        Zone? ultimaZona = null,
        DateTime? ultimoAvisoCompra = null,
        DateTime? ultimoAvisoVenda = null) =>
        new(Id: 1, Ticker: "PETR4", Email: "a@b.com", CancelToken: "tok",
            Active: true, CreatedAt: Agora.AddDays(-1), CancelledAt: null,
            LastZone: ultimaZona,
            LastBuyNoticeAt: ultimoAvisoCompra,
            LastSellNoticeAt: ultimoAvisoVenda);

    private static AlertDecision Avaliar(Subscription inscricao, decimal preco, DateTime? quando = null) =>
        AlertEngine.Evaluate(inscricao, preco, Compra, Venda, quando ?? Agora, Cooldown);

    [Fact]
    public void Avisa_venda_quando_o_preco_passa_da_linha_de_venda()
    {
        var decision = Avaliar(Inscricao(), 22.80m);

        Assert.Equal(Zone.Venda, decision.Zone);
        Assert.True(decision.ShouldNotify);
        Assert.Equal(AlertType.Sell, decision.Type);
        Assert.Equal(Venda, decision.Threshold);
    }

    [Fact]
    public void Avisa_compra_quando_o_preco_cai_abaixo_da_linha_de_compra()
    {
        var decision = Avaliar(Inscricao(), 22.50m);

        Assert.Equal(Zone.Compra, decision.Zone);
        Assert.True(decision.ShouldNotify);
        Assert.Equal(AlertType.Buy, decision.Type);
        Assert.Equal(Compra, decision.Threshold);
    }

    [Fact]
    public void Nao_avisa_na_faixa_neutra()
    {
        var decision = Avaliar(Inscricao(), 22.60m);

        Assert.Equal(Zone.Neutra, decision.Zone);
        Assert.False(decision.ShouldNotify);
    }

    [Fact]
    public void Preco_igual_ao_limite_conta_como_neutro()
    {
        // Mesmo critério da Etapa 1: usa > e <, nunca >= ou <=.
        Assert.Equal(Zone.Neutra, Avaliar(Inscricao(), Venda).Zone);
        Assert.Equal(Zone.Neutra, Avaliar(Inscricao(), Compra).Zone);
    }

    [Fact]
    public void Nao_repete_o_aviso_enquanto_continua_na_mesma_faixa()
    {
        var jaAvisada = Inscricao(ultimaZona: Zone.Venda, ultimoAvisoVenda: Agora);

        var decision = Avaliar(jaAvisada, 22.90m);

        Assert.False(decision.ShouldNotify);
    }

    [Fact]
    public void Avisa_de_novo_apos_sair_e_voltar_para_a_faixa()
    {
        // Saiu para a neutra (zona gravada como Neutra) e voltou para a de venda.
        var voltou = Inscricao(ultimaZona: Zone.Neutra, ultimoAvisoVenda: Agora);

        var decision = Avaliar(voltou, 22.80m);

        Assert.True(decision.ShouldNotify);
    }

    [Fact]
    public void Avisa_ao_pular_direto_de_uma_faixa_para_a_outra()
    {
        // Salto grande sem passar pela neutra: é um cruzamento legítimo.
        var estavaVendendo = Inscricao(ultimaZona: Zone.Venda, ultimoAvisoVenda: Agora);

        var decision = Avaliar(estavaVendendo, 20.00m);

        Assert.Equal(Zone.Compra, decision.Zone);
        Assert.True(decision.ShouldNotify);
    }

    [Fact]
    public void Reenvia_lembrete_quando_o_cooldown_vence()
    {
        var jaAvisada = Inscricao(ultimaZona: Zone.Venda, ultimoAvisoVenda: Agora);

        var decision = Avaliar(jaAvisada, 22.90m, Agora.Add(Cooldown));

        Assert.True(decision.ShouldNotify);
    }

    [Fact]
    public void O_cooldown_e_separado_por_tipo_de_aviso()
    {
        // Avisou venda há pouco; cair para a faixa de compra deve avisar na hora,
        // porque o cooldown de compra nunca foi usado.
        var so_venda = Inscricao(ultimaZona: Zone.Venda, ultimoAvisoVenda: Agora);

        var decision = Avaliar(so_venda, 22.00m, Agora.AddMinutes(1));

        Assert.Equal(Zone.Compra, decision.Zone);
        Assert.True(decision.ShouldNotify);
    }

    [Fact]
    public void Rejeita_limites_invertidos()
    {
        Assert.Throws<ArgumentException>(() =>
            AlertEngine.Evaluate(Inscricao(), 22.60m,
                buyThreshold: 30m, sellThreshold: 20m, Agora, Cooldown));
    }

    [Fact]
    public void ApplyNotified_grava_a_faixa_e_a_hora_do_tipo_certo()
    {
        var inscricao = Inscricao();

        var depois = AlertEngine.ApplyNotified(inscricao, Zone.Venda, Agora);

        Assert.Equal(Zone.Venda, depois.LastZone);
        Assert.Equal(Agora, depois.LastSellNoticeAt);
        Assert.Null(depois.LastBuyNoticeAt); // o cooldown de compra não foi tocado
    }
}
