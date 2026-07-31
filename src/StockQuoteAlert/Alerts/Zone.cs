namespace StockQuoteAlert.Alerts;

/// <summary>
/// Em que faixa o preço está agora, em relação aos limites do ativo.
/// Guardado como texto no banco ("COMPRA", "VENDA", "NEUTRA") para ficar
/// legível quando você abrir o arquivo do banco para investigar algo.
/// </summary>
public enum Zone
{
    /// <summary>Preço abaixo da linha de compra: está barato.</summary>
    Compra,

    /// <summary>Preço entre as duas linhas: nada a fazer.</summary>
    Neutra,

    /// <summary>Preço acima da linha de venda: está caro.</summary>
    Venda
}

public static class ZoneExtensions
{
    public static string ToDb(this Zone zone) => zone switch
    {
        Zone.Compra => "COMPRA",
        Zone.Venda => "VENDA",
        _ => "NEUTRA"
    };

    public static Zone? FromDb(string? value) => value switch
    {
        "COMPRA" => Zone.Compra,
        "VENDA" => Zone.Venda,
        "NEUTRA" => Zone.Neutra,
        _ => null
    };

    /// <summary>Converte para o tipo de alerta usado no e-mail (compra ou venda).</summary>
    public static AlertType ToAlertType(this Zone zone) =>
        zone == Zone.Venda ? AlertType.Sell : AlertType.Buy;
}
