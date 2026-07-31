using System.Globalization;

namespace StockQuoteAlert.Data;

/// <summary>
/// Converte valores entre o C# e o formato que gravamos no banco.
///
/// Duas regras que valem para o projeto inteiro:
///
/// DINHEIRO vira texto no formato invariante ("41.21", sempre com ponto).
/// Se gravássemos como número de ponto flutuante, 0,10 viraria 0,09999999.
/// Se gravássemos usando a cultura pt-BR, sairia "41,21" — e um banco lido
/// noutra máquina interpretaria errado. Texto invariante resolve os dois.
///
/// DATAS vão sempre em UTC, no formato ISO 8601 ("2026-07-29T02:57:21.7120000Z").
/// Convertemos para o horário de Brasília só na hora de mostrar para a pessoa.
/// Guardar no fuso local dá problema quando o servidor está noutro país.
/// </summary>
internal static class DbValue
{
    public static string FromMoney(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    public static string? FromMoney(decimal? value) =>
        value is null ? null : FromMoney(value.Value);

    public static decimal ToMoney(string text) =>
        decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    public static decimal? ToMoneyOrNull(object? value) =>
        value is null or DBNull ? null : ToMoney((string)value);

    public static string FromDate(DateTime value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    public static string? FromDate(DateTime? value) =>
        value is null ? null : FromDate(value.Value);

    // Gravamos no formato "o", que termina em Z. RoundtripKind já devolve o
    // DateTime marcado como UTC — não dá para combinar com AdjustToUniversal.
    public static DateTime ToDate(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTime? ToDateOrNull(object? value) =>
        value is null or DBNull ? null : ToDate((string)value);

    public static string? ToStringOrNull(object? value) =>
        value is null or DBNull ? null : (string)value;
}
