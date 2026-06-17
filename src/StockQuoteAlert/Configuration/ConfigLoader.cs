using System.Text.Json;

namespace StockQuoteAlert.Configuration;

/// <summary>
/// Lê e desserializa o arquivo de configuração JSON.
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Arquivo de configuração não encontrado: '{path}'. " +
                "Use 'config.example.json' como base.", path);

        string json = File.ReadAllText(path);

        AppSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json, Options)
                       ?? throw new InvalidOperationException("Configuração vazia.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Falha ao interpretar o JSON de configuração: {ex.Message}", ex);
        }

        settings.Validate();
        return settings;
    }
}
