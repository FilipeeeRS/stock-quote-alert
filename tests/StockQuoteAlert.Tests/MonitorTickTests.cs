using Microsoft.Data.Sqlite;
using StockQuoteAlert.Alerts;
using StockQuoteAlert.Configuration;
using StockQuoteAlert.Data;
using StockQuoteAlert.Monitoring;
using Xunit;

namespace StockQuoteAlert.Tests;

/// <summary>
/// Testes da rodada do worker, usando um banco SQLite de verdade num arquivo
/// temporário. Sem internet e sem envio de e-mail: a cotação e o notificador
/// são falsos, e o relógio é injetado.
/// </summary>
public class MonitorTickTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Database _db;
    private readonly SubscriptionRepository _subscriptions;
    private readonly AssetRepository _assets;
    private readonly NoticeRepository _notices;

    private static readonly DateTime Agora = new(2026, 7, 29, 14, 0, 0, DateTimeKind.Utc);

    /// <summary>Histórico 1..100 → limite de compra 20,80 e de venda 80,20.</summary>
    private static readonly decimal[] Historico =
        Enumerable.Range(1, 100).Select(i => (decimal)i).ToArray();

    public MonitorTickTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sqa-test-{Guid.NewGuid():N}.db");
        _db = new Database(_dbPath);
        _db.EnsureCreated();

        _subscriptions = new SubscriptionRepository(_db);
        _assets = new AssetRepository(_db);
        _notices = new NoticeRepository(_db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { /* o SO libera depois */ }
    }

    private MonitorTick BuildTick(FakeQuoteProvider quotes, RecordingSubscriberNotifier notifier,
        DateTime? clock = null, int percentilCompra = 20, int percentilVenda = 80) =>
        new(quotes, notifier, _subscriptions, _assets, _notices,
            new MonitoringSettings
            {
                DelayBetweenTickersMs = 0,
                BuyPercentile = percentilCompra,
                SellPercentile = percentilVenda
            },
            TimeSpan.FromMinutes(15),
            () => clock ?? Agora);

    // ---------------------------------------------------------------

    [Fact]
    public async Task Consulta_a_api_uma_vez_por_ativo_mesmo_com_varios_inscritos()
    {
        // Esta é a economia de cota que torna o projeto viável no plano gratuito.
        _subscriptions.Add("PETR4", "um@teste.com", Agora);
        _subscriptions.Add("PETR4", "dois@teste.com", Agora);
        _subscriptions.Add("PETR4", "tres@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, Historico); // acima de 80,20 → faixa de venda
        var notifier = new RecordingSubscriberNotifier();

        var result = await BuildTick(quotes, notifier).RunOnceAsync(default);

        Assert.Equal(1, quotes.SnapshotCalls);   // uma chamada à API...
        Assert.Equal(3, notifier.Sent.Count);    // ...e três e-mails
        Assert.Equal(3, result.NoticesSent);
    }

    [Fact]
    public async Task Nao_reenvia_enquanto_o_preco_continua_na_mesma_faixa()
    {
        _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, Historico);
        var notifier = new RecordingSubscriberNotifier();
        var tick = BuildTick(quotes, notifier);

        await tick.RunOnceAsync(default);
        await tick.RunOnceAsync(default);
        await tick.RunOnceAsync(default);

        Assert.Single(notifier.Sent);
    }

    [Fact]
    public async Task Reiniciar_o_worker_nao_reenvia_o_mesmo_aviso()
    {
        // Melhoria em relação à Etapa 1: lá o estado vivia na memória, então
        // reiniciar o programa mandava o e-mail de novo. Agora ele vem do banco.
        _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, Historico);

        var primeiro = new RecordingSubscriberNotifier();
        await BuildTick(quotes, primeiro).RunOnceAsync(default);
        Assert.Single(primeiro.Sent);

        // "Reinício": instância nova do worker, mesmo banco.
        var segundo = new RecordingSubscriberNotifier();
        await BuildTick(quotes, segundo).RunOnceAsync(default);

        Assert.Empty(segundo.Sent);
    }

    [Fact]
    public async Task Avisa_de_novo_quando_o_preco_sai_e_volta_para_a_faixa()
    {
        _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        var notifier = new RecordingSubscriberNotifier();

        quotes.Set("PETR4", 90m, Historico);   // faixa de venda → avisa
        await BuildTick(quotes, notifier).RunOnceAsync(default);

        quotes.Set("PETR4", 50m, Historico);   // faixa neutra → silêncio
        await BuildTick(quotes, notifier).RunOnceAsync(default);

        quotes.Set("PETR4", 90m, Historico);   // voltou → novo cruzamento
        await BuildTick(quotes, notifier).RunOnceAsync(default);

        Assert.Equal(2, notifier.Sent.Count);
        Assert.All(notifier.Sent, a => Assert.Equal(AlertType.Sell, a.Type));
    }

    [Fact]
    public async Task Um_ativo_com_falha_nao_impede_os_outros()
    {
        // Regra da Etapa 1: falha de rede/API não derruba o processo.
        _subscriptions.Add("PETR4", "um@teste.com", Agora);
        _subscriptions.Add("VALE3", "dois@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("VALE3", 90m, Historico);   // PETR4 fica de fora → consulta falha
        var notifier = new RecordingSubscriberNotifier();

        var result = await BuildTick(quotes, notifier).RunOnceAsync(default);

        Assert.Single(notifier.Sent);
        Assert.Equal("dois@teste.com", notifier.Sent[0].Email);
        Assert.Equal(2, result.TickersChecked);
    }

    [Fact]
    public async Task Falha_no_envio_nao_marca_como_avisado_e_tenta_de_novo()
    {
        _subscriptions.Add("PETR4", "quebra@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, Historico);

        var comFalha = new RecordingSubscriberNotifier { FailFor = "quebra@teste.com" };
        var result = await BuildTick(quotes, comFalha).RunOnceAsync(default);

        Assert.Empty(comFalha.Sent);
        Assert.Equal(1, result.SendFailures);

        // Próxima rodada: como o estado não foi gravado, tenta outra vez.
        var semFalha = new RecordingSubscriberNotifier();
        await BuildTick(quotes, semFalha).RunOnceAsync(default);

        Assert.Single(semFalha.Sent);
    }

    [Fact]
    public async Task Inscricao_cancelada_nao_recebe_mais_nada()
    {
        var inscricao = _subscriptions.Add("PETR4", "saiu@teste.com", Agora);
        Assert.NotNull(inscricao);

        string? email = _subscriptions.CancelByToken(inscricao!.CancelToken, Agora);
        Assert.Equal("saiu@teste.com", email);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, Historico);
        var notifier = new RecordingSubscriberNotifier();

        var result = await BuildTick(quotes, notifier).RunOnceAsync(default);

        Assert.Empty(notifier.Sent);
        Assert.Equal(0, quotes.SnapshotCalls); // nem consulta a API: ninguém acompanha
        Assert.Equal(0, result.TickersChecked);
    }

    [Fact]
    public async Task Grava_o_preco_e_os_limites_calculados_no_banco()
    {
        _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, Historico);

        await BuildTick(quotes, new RecordingSubscriberNotifier()).RunOnceAsync(default);

        Asset? asset = _assets.Get("PETR4");

        Assert.NotNull(asset);
        Assert.Equal(90m, asset!.CurrentPrice);
        Assert.Equal(20.80m, asset.BuyThreshold);
        Assert.Equal(80.20m, asset.SellThreshold);
        Assert.True(asset.HasUsableThresholds);
    }

    [Fact]
    public async Task Reaproveita_os_limites_enquanto_a_configuracao_nao_muda()
    {
        // Refazer a conta a cada rodada seria desperdicio: o historico e diario.
        _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 50m, Historico);

        await BuildTick(quotes, new RecordingSubscriberNotifier()).RunOnceAsync(default);
        DateTime? primeiroCalculo = _assets.Get("PETR4")!.ThresholdsComputedAt;

        await BuildTick(quotes, new RecordingSubscriberNotifier()).RunOnceAsync(default);

        Assert.Equal(primeiroCalculo, _assets.Get("PETR4")!.ThresholdsComputedAt);
        Assert.Equal(80.20m, _assets.Get("PETR4")!.SellThreshold);
    }

    [Fact]
    public async Task Recalcula_os_limites_quando_os_percentis_mudam_no_config()
    {
        // Sem isto, mexer em 'sellPercentile' no config.json não surtia efeito
        // nenhum até o prazo de 24h vencer — e sem nenhuma mensagem explicando.
        _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 50m, Historico);

        await BuildTick(quotes, new RecordingSubscriberNotifier()).RunOnceAsync(default);
        Assert.Equal(80.20m, _assets.Get("PETR4")!.SellThreshold);

        // Mesma rodada, mesmo instante: só a configuração mudou.
        await BuildTick(quotes, new RecordingSubscriberNotifier(), percentilVenda: 30)
            .RunOnceAsync(default);

        Asset? depois = _assets.Get("PETR4");
        Assert.Equal(30.70m, depois!.SellThreshold);   // percentil 30 de 1..100
        Assert.Equal(30, depois.SellPercentile);        // e fica registrado de onde veio
    }

    [Fact]
    public async Task Registra_o_aviso_no_historico()
    {
        var inscricao = _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, Historico);

        await BuildTick(quotes, new RecordingSubscriberNotifier()).RunOnceAsync(default);

        Assert.Equal(1, _notices.CountBySubscription(inscricao!.Id));
    }

    [Fact]
    public async Task Sem_historico_suficiente_nao_avisa_ninguem()
    {
        _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, new decimal[] { 10m, 20m, 30m }); // curto demais
        var notifier = new RecordingSubscriberNotifier();

        await BuildTick(quotes, notifier).RunOnceAsync(default);

        Assert.Empty(notifier.Sent);

        // Mas o preço lido é gravado, para você conseguir investigar depois.
        Assert.Equal(90m, _assets.Get("PETR4")!.CurrentPrice);
    }

    [Fact]
    public async Task O_token_de_cancelamento_vai_junto_no_aviso()
    {
        var inscricao = _subscriptions.Add("PETR4", "um@teste.com", Agora);

        var quotes = new FakeQuoteProvider();
        quotes.Set("PETR4", 90m, Historico);
        var notifier = new RecordingSubscriberNotifier();

        await BuildTick(quotes, notifier).RunOnceAsync(default);

        Assert.Equal(inscricao!.CancelToken, notifier.Sent[0].CancelToken);
    }
}
