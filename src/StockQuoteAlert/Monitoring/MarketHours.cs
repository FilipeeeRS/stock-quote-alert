namespace StockQuoteAlert.Monitoring;

/// <summary>
/// Diz se a B3 está em pregão agora.
///
/// POR QUE ISSO EXISTE: fora do pregão o preço não muda, então consultar a API
/// seria queimar cota à toa. Como o plano gratuito dá 15.000 requisições por mês,
/// dormir à noite e no fim de semana multiplica por ~5 quantos ativos cabem.
///
/// O que NÃO tratamos aqui: feriados da B3. Manter essa lista atualizada todo ano
/// é trabalho, e o prejuízo de errar é pequeno — algumas consultas desperdiçadas
/// num feriado, lendo o preço do último fechamento. Fica para quando incomodar.
/// </summary>
public static class MarketHours
{
    // Pregão regular da B3: 10h às 17h (horário de Brasília).
    private static readonly TimeSpan Open = new(10, 0, 0);
    private static readonly TimeSpan Close = new(17, 0, 0);

    public static bool IsOpen(DateTime utcNow)
    {
        DateTime local = ToBrasilia(utcNow);

        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        TimeSpan time = local.TimeOfDay;
        return time >= Open && time < Close;
    }

    public static DateTime ToBrasilia(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), BrasiliaZone);

    /// <summary>
    /// O identificador do fuso muda conforme o sistema: Windows usa um nome,
    /// Linux e macOS usam outro. Tentamos os dois para o programa rodar
    /// tanto na sua máquina quanto no servidor onde ele for publicado.
    /// </summary>
    private static readonly TimeZoneInfo BrasiliaZone = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        foreach (string id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // tenta o próximo
            }
        }

        // Último recurso: Brasília sem horário de verão (que o Brasil não usa desde 2019).
        Console.Error.WriteLine(
            "[aviso] Fuso de Brasília não encontrado no sistema; usando UTC-3 fixo.");
        return TimeZoneInfo.CreateCustomTimeZone("BRT-fallback", TimeSpan.FromHours(-3),
            "Brasilia (fallback)", "Brasilia (fallback)");
    }
}
