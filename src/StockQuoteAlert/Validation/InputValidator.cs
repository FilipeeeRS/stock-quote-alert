using System.Net.Mail;

namespace StockQuoteAlert.Validation;

/// <summary>
/// Validação de ativo e e-mail, num lugar só.
///
/// Fica aqui — e não dentro do site ou da linha de comando — porque as duas
/// portas de entrada precisam das MESMAS regras. Se a validação morasse só na
/// página, bastaria alguém chamar a API direto para escapar dela.
/// </summary>
public static class InputValidator
{
    /// <summary>Ativos da B3 têm 4 letras + 1 ou 2 dígitos (PETR4, TAEE11).</summary>
    public static bool IsValidTicker(string? ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return false;

        string t = ticker.Trim();

        if (t.Length is < 5 or > 6)
            return false;

        for (int i = 0; i < 4; i++)
            if (!char.IsAsciiLetter(t[i]))
                return false;

        for (int i = 4; i < t.Length; i++)
            if (!char.IsAsciiDigit(t[i]))
                return false;

        return true;
    }

    public static string NormalizeTicker(string ticker) => ticker.Trim().ToUpperInvariant();

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return false;

        // Limite prático: evita que alguém grave um texto gigante no banco.
        if (email.Length > 254)
            return false;

        try
        {
            var parsed = new MailAddress(email.Trim());
            return parsed.Address == email.Trim();
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
