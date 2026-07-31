using StockQuoteAlert.Validation;
using Xunit;

namespace StockQuoteAlert.Tests;

/// <summary>
/// Validação de entrada. Importa mais do que parece: o site chama a mesma API
/// que qualquer pessoa pode chamar direto, então a regra tem que valer aqui,
/// não só no formulário da página.
/// </summary>
public class InputValidatorTests
{
    [Theory]
    [InlineData("PETR4")]
    [InlineData("VALE3")]
    [InlineData("TAEE11")]  // units têm dois dígitos
    [InlineData("petr4")]   // minúsculo é aceito; normalizamos depois
    public void Aceita_tickers_validos(string ticker)
    {
        Assert.True(InputValidator.IsValidTicker(ticker));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("PETR")]        // sem dígito
    [InlineData("PET4")]        // poucas letras
    [InlineData("PETR456")]     // dígitos demais
    [InlineData("PE1R4")]       // dígito no meio das letras
    [InlineData("PETR4; DROP TABLE Inscricoes")]
    public void Recusa_tickers_invalidos(string? ticker)
    {
        Assert.False(InputValidator.IsValidTicker(ticker));
    }

    [Fact]
    public void Normaliza_para_maiusculas()
    {
        Assert.Equal("PETR4", InputValidator.NormalizeTicker("  petr4 "));
    }

    [Theory]
    [InlineData("filipe@exemplo.com")]
    [InlineData("nome.sobrenome+tag@sub.dominio.com.br")]
    public void Aceita_emails_validos(string email)
    {
        Assert.True(InputValidator.IsValidEmail(email));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("sem-arroba")]
    [InlineData("@semnome.com")]
    [InlineData("espaco no meio@exemplo.com")]
    public void Recusa_emails_invalidos(string? email)
    {
        Assert.False(InputValidator.IsValidEmail(email));
    }

    [Fact]
    public void Recusa_email_absurdamente_longo()
    {
        // Evita que alguém grave um texto gigante no banco.
        string gigante = new string('a', 250) + "@exemplo.com";

        Assert.False(InputValidator.IsValidEmail(gigante));
    }
}
