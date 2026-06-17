using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using StockQuoteAlert.Configuration;

namespace StockQuoteAlert.Quotes;

/// <summary>
/// Provedor de cotações baseado na API pública brapi.dev (mercado B3).
/// Endpoint: GET /api/quote/{ticker} -> results[0].regularMarketPrice
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
        string url = _api.BaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(ticker);

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

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.Error.WriteLine($"[brapi] Ativo '{ticker}' não encontrado (404).");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[brapi] Resposta HTTP {(int)response.StatusCode} ao consultar {ticker}.");
            return null;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var payload = JsonSerializer.Deserialize<BrapiResponse>(body);
            var price = payload?.Results?.FirstOrDefault()?.RegularMarketPrice;

            if (price is null)
                Console.Error.WriteLine($"[brapi] Cotação ausente na resposta para {ticker}.");

            return price;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[brapi] Falha ao interpretar resposta para {ticker}: {ex.Message}");
            return null;
        }
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
    }
}
