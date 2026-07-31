using Microsoft.Extensions.Caching.Memory;
using StockQuoteAlert.Alerts;
using StockQuoteAlert.Analysis;
using StockQuoteAlert.Configuration;
using StockQuoteAlert.Data;
using StockQuoteAlert.Monitoring;
using StockQuoteAlert.Notifications;
using StockQuoteAlert.Quotes;
using StockQuoteAlert.Validation;
using StockQuoteAlert.Web;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuração: procura o config.json em alguns lugares conhecidos, para você
// não precisar duplicar o arquivo que já usa no projeto de console.
// ---------------------------------------------------------------------------
string configPath = ResolveConfigPath(builder.Environment.ContentRootPath);
AppSettings settings = ConfigLoader.LoadOrDefault(configPath);

// ---------------------------------------------------------------------------
// Dependências. Tudo singleton: os repositórios não guardam estado, abrem e
// fecham a conexão a cada operação.
// ---------------------------------------------------------------------------
var database = new Database(settings.Database.Path);
database.EnsureCreated();

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(database);
builder.Services.AddSingleton(new SubscriptionRepository(database));
builder.Services.AddSingleton(new AssetRepository(database));
builder.Services.AddSingleton(new NoticeRepository(database));
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient("brapi", client => client.Timeout = TimeSpan.FromSeconds(15));

builder.Services.AddSingleton<IQuoteProvider>(sp =>
    new BrapiQuoteProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("brapi"),
        settings.Api));

builder.Services.AddSingleton<ISubscriberNotifier>(_ =>
    new EmailNotifier(settings.Smtp, settings.AlertRecipient, ResolveCancelBaseUrl(settings)));

builder.Services.AddSingleton(sp => new MonitorTick(
    sp.GetRequiredService<IQuoteProvider>(),
    sp.GetRequiredService<ISubscriberNotifier>(),
    sp.GetRequiredService<SubscriptionRepository>(),
    sp.GetRequiredService<AssetRepository>(),
    sp.GetRequiredService<NoticeRepository>(),
    settings.Monitoring,
    TimeSpan.FromMinutes(settings.AlertCooldownMinutes)));

builder.Services.AddHostedService<MonitorBackgroundService>();

var app = builder.Build();

if (string.IsNullOrWhiteSpace(settings.Smtp.Host))
{
    app.Logger.LogWarning(
        "SMTP não configurado ({Caminho} não encontrado ou incompleto). O site funciona " +
        "e aceita inscrições, mas nenhum e-mail será enviado até você preencher o config.json.",
        configPath);
}

app.UseDefaultFiles();
app.UseStaticFiles();

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------

// Busca a cotação e já mostra os limites que serão usados para avisar.
app.MapGet("/api/cotacao/{ticker}", async (
    string ticker,
    IQuoteProvider quotes,
    IMemoryCache cache,
    CancellationToken cancellationToken) =>
{
    if (!InputValidator.IsValidTicker(ticker))
        return Results.BadRequest(new { erro = "Ativo inválido. Use algo como PETR4 ou TAEE11." });

    string symbol = InputValidator.NormalizeTicker(ticker);

    // Guarda a resposta por 5 minutos. Sem isto, cada pesquisa de cada visitante
    // gastaria uma requisição da cota mensal da brapi — e um F5 insistente
    // esvaziaria a cota sozinho. Como a cotação gratuita já vem com ~30 min de
    // atraso, este cache não esconde nenhuma informação nova.
    if (cache.TryGetValue(symbol, out QuoteResponse? cached) && cached is not null)
        return Results.Ok(cached);

    QuoteSnapshot? snapshot =
        await quotes.GetSnapshotAsync(symbol, settings.Monitoring.HistoryRange, cancellationToken);

    if (snapshot is null)
        return Results.NotFound(new
        {
            erro = $"Não foi possível obter a cotação de {symbol}. " +
                   "Verifique o código do ativo — e note que, sem um token da brapi, " +
                   "só PETR4, VALE3, ITUB4 e MGLU3 funcionam."
        });

    Thresholds? thresholds = ThresholdCalculator.Compute(
        snapshot.ClosingPrices,
        settings.Monitoring.BuyPercentile,
        settings.Monitoring.SellPercentile);

    // Em que faixa o preço já está agora. Sem isto a tela diria "avisamos se
    // cair abaixo de X" para um ativo que JÁ está abaixo de X, o que confunde.
    string? zonaAtual = thresholds is null
        ? null
        : AlertEngine.ClassifyZone(snapshot.Price, thresholds.Buy, thresholds.Sell) switch
        {
            Zone.Compra => "compra",
            Zone.Venda => "venda",
            _ => "neutra"
        };

    var response = new QuoteResponse(
        symbol,
        snapshot.Price,
        thresholds?.Buy,
        thresholds?.Sell,
        zonaAtual,
        // O histórico vai junto para a página poder desenhar o gráfico. Não custa
        // requisição extra: veio na mesma chamada que trouxe o preço.
        ReduzirHistorico(snapshot.ClosingPrices, 90),
        thresholds is null
            ? "Ainda não há histórico suficiente para calcular os limites deste ativo."
            : null);

    cache.Set(symbol, response, TimeSpan.FromMinutes(5));
    return Results.Ok(response);
});

// Cria a inscrição. Não pede senha nem cadastro: só ativo e e-mail.
app.MapPost("/api/inscricoes", (
    SubscribeRequest request,
    SubscriptionRepository subscriptions) =>
{
    if (!InputValidator.IsValidTicker(request.Ticker))
        return Results.BadRequest(new { erro = "Ativo inválido." });

    if (!InputValidator.IsValidEmail(request.Email))
        return Results.BadRequest(new { erro = "E-mail inválido." });

    string symbol = InputValidator.NormalizeTicker(request.Ticker!);
    string email = request.Email!.Trim();

    Subscription? created = subscriptions.Add(symbol, email, DateTime.UtcNow);

    if (created is null)
        return Results.Conflict(new { erro = $"{email} já acompanha {symbol}." });

    return Results.Ok(new
    {
        mensagem = $"Pronto! Avisaremos {email} quando {symbol} ficar interessante.",
        ticker = symbol
    });
});

// Cancela pelo token que vai no e-mail.
app.MapPost("/api/cancelar", (CancelRequest request, SubscriptionRepository subscriptions) =>
{
    if (string.IsNullOrWhiteSpace(request.Token))
        return Results.BadRequest(new { erro = "Token não informado." });

    string? email = subscriptions.CancelByToken(request.Token.Trim(), DateTime.UtcNow);

    return email is null
        ? Results.NotFound(new { erro = "Este link já foi usado ou não é válido." })
        : Results.Ok(new { mensagem = $"Pronto. {email} não receberá mais estes avisos." });
});

// A página de cancelamento, para o link do e-mail ficar bonito (/cancelar?token=…).
app.MapGet("/cancelar", (HttpContext http) =>
    Results.File(
        Path.Combine(app.Environment.WebRootPath, "cancelar.html"), "text/html"));

app.Run();

// ---------------------------------------------------------------------------

static string ResolveConfigPath(string contentRoot)
{
    string? fromEnv = Environment.GetEnvironmentVariable("STOCK_ALERT_CONFIG");
    if (!string.IsNullOrWhiteSpace(fromEnv))
        return fromEnv;

    string[] candidates =
    [
        Path.Combine(contentRoot, "config.json"),
        // O mesmo config.json que o projeto de console já usa.
        Path.Combine(contentRoot, "..", "StockQuoteAlert", "config.json")
    ];

    foreach (string candidate in candidates)
        if (File.Exists(candidate))
            return Path.GetFullPath(candidate);

    return candidates[0];
}

/// <summary>
/// O e-mail precisa de um endereço absoluto no botão Cancelar. Em produção você
/// preenche 'cancelBaseUrl' no config.json; rodando local, cai no localhost.
/// </summary>
/// <summary>
/// Reduz o histórico a no máximo N pontos, pegando de forma espaçada. Seis meses
/// dão ~120 dias; para um gráfico pequeno isso é mais detalhe do que a tela mostra,
/// e mandar tudo só engorda a resposta. Mantém sempre o primeiro e o último dia.
/// </summary>
static IReadOnlyList<decimal> ReduzirHistorico(IReadOnlyList<decimal> valores, int maximo)
{
    if (valores.Count <= maximo)
        return valores;

    var reduzido = new List<decimal>(maximo);
    double passo = (valores.Count - 1) / (double)(maximo - 1);

    for (int i = 0; i < maximo; i++)
        reduzido.Add(valores[(int)Math.Round(i * passo)]);

    return reduzido;
}

static string ResolveCancelBaseUrl(AppSettings settings) =>
    string.IsNullOrWhiteSpace(settings.CancelBaseUrl)
        ? "http://localhost:5000/cancelar"
        : settings.CancelBaseUrl;

internal sealed record SubscribeRequest(string? Ticker, string? Email);
internal sealed record CancelRequest(string? Token);
internal sealed record QuoteResponse(
    string Ticker,
    decimal Preco,
    decimal? LimiteCompra,
    decimal? LimiteVenda,
    string? ZonaAtual,
    IReadOnlyList<decimal> Historico,
    string? Aviso);
