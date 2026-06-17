namespace StockQuoteAlert.Alerts;

public enum AlertType
{
    /// <summary>Preço acima da linha azul: aconselha venda.</summary>
    Sell,

    /// <summary>Preço abaixo da linha vermelha: aconselha compra.</summary>
    Buy
}
