using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using StockQuoteAlert.Configuration;

namespace StockQuoteAlert.Quotes;

/// <summary>
/// Provedor de cotações baseado na API pública brapi.dev (mercado B3).
/// Endpoint: GET /api/quote/{ticker} -> results[0].regularMarketPrice
/// Com ?range=6mo&amp;interval=1d vem também results[0].historicalDataPrice.
/// </summary>
public sealed class BrapiQuoteProvider : IQuoteProvider
{
    private readonly HttpClient _http;
    private readonly ApiSettings _api;

    public BrapiQuoteProvider(HttpClient http, ApiSettings api)
    {
        _http = http;
        _api = api;

        if (!string.IsNullOrWhiteSpace(_api.Token))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _api.Token);
    }

    public async Task<decimal?> GetCurrentPriceAsync(string ticker, CancellationToken cancellationToken)
    {
        BrapiResult? result = await FetchAsync(ticker, range: null, cancellationToken);
        return result?.RegularMarketPrice;
    }

    public async Task<QuoteSnapshot?> GetSnapshotAsync(string ticker, string range,
        CancellationToken cancellationToken)
    {
        BrapiResult? result = await FetchAsync(ticker, range, cancellationToken);

        if (result?.RegularMarketPrice is null)
            return null;

        // Alguns dias podem vir sem fechamento (feriado, pregão interrompido); descartamos.
        var closes = (result.HistoricalDataPrice ?? new List<BrapiHistoryPoint>())
            .Where(p => p.Close is > 0)
            .Select(p => p.Close!.Value)
            .ToList();

        return new QuoteSnapshot(ticker, result.RegularMarketPrice.Value, closes);
    }

    private async Task<BrapiResult?> FetchAsync(string ticker, string? range,
        CancellationToken cancellationToken)
    {
        string url = _api.BaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(ticker);
        if (!string.IsNullOrWhiteSpace(range))
            url += $"?range={Uri.EscapeDataString(range)}&interval=1d";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Falha de rede/timeout: não derruba o loop, apenas reporta ausência de cotação.
            Console.Error.WriteLine($"[brapi] Erro de rede ao consultar {ticker}: {ex.Message}");
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                ReportFailure(ticker, response.StatusCode);
                return null;
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                var payload = JsonSerializer.Deserialize<BrapiResponse>(body);
                var result = payload?.Results?.FirstOrDefault();

                if (result?.RegularMarketPrice is null)
                    Console.Error.WriteLine($"[brapi] Cotação ausente na resposta para {ticker}.");

                return result;
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"[brapi] Falha ao interpretar resposta para {ticker}: {ex.Message}");
                return null;
            }
        }
    }

    private static void ReportFailure(string ticker, HttpStatusCode status)
    {
        // Atenção: a brapi responde 401 (não 404) quando o ativo não está liberado
        // sem token. Sem token funcionam apenas PETR4, VALE3, ITUB4 e MGLU3.
        string message = status switch
        {
            HttpStatusCode.Unauthorized =>
                $"'{ticker}' exige um token da brapi. Sem token, apenas PETR4, VALE3, ITUB4 " +
                "e MGLU3 funcionam. Crie uma conta em brapi.dev e preencha 'api.token' no config.json.",

            HttpStatusCode.NotFound =>
                $"Ativo '{ticker}' não encontrado.",

            HttpStatusCode.TooManyRequests =>
                $"Limite de requisições da brapi excedido ao consultar {ticker}. " +
                "Aumente 'tickIntervalMinutes' ou monitore menos ativos.",

            _ => $"Resposta HTTP {(int)status} ao consultar {ticker}."
        };

        Console.Error.WriteLine($"[brapi] {message}");
    }

    // DTOs internos do payload da brapi.
    private sealed class BrapiResponse
    {
        [JsonPropertyName("results")]
        public List<BrapiResult>? Results { get; set; }
    }

    private sealed class BrapiResult
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("regularMarketPrice")]
        public decimal? RegularMarketPrice { get; set; }

        [JsonPropertyName("historicalDataPrice")]
        public List<BrapiHistoryPoint>? HistoricalDataPrice { get; set; }
    }

    private sealed class BrapiHistoryPoint
    {
        [JsonPropertyName("close")]
        public decimal? Close { get; set; }
    }
}
