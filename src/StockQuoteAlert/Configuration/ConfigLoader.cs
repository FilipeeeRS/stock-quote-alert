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

    /// <param name="requireRecipient">
    /// O modo da Etapa 1 exige 'alertRecipient' no config.json; o modo de
    /// inscrições pega os destinatários do banco e dispensa esse campo.
    /// </param>
    public static AppSettings Load(string path, bool requireRecipient = true)
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

        settings.Validate(requireRecipient);
        return settings;
    }

    /// <summary>
    /// Lê a configuração sem exigir nada. Serve para os comandos que só mexem
    /// no banco (inscrever, listar, cancelar): assim você consegue testar o
    /// banco antes de ter configurado o envio de e-mail.
    /// </summary>
    public static AppSettings LoadOrDefault(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options)
                   ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Falha ao interpretar o JSON de configuração: {ex.Message}", ex);
        }
    }
}
