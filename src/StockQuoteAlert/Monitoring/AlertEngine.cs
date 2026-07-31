using StockQuoteAlert.Alerts;
using StockQuoteAlert.Data;

namespace StockQuoteAlert.Monitoring;

/// <summary>O que fazer com uma inscrição depois de olhar o preço atual.</summary>
/// <param name="Zone">Em que faixa o preço caiu agora.</param>
/// <param name="ShouldNotify">Se deve mandar e-mail.</param>
/// <param name="Type">Compra ou venda (só preenchido quando vai notificar).</param>
/// <param name="Threshold">O limite que foi cruzado (só quando vai notificar).</param>
public sealed record AlertDecision(
    Zone Zone,
    bool ShouldNotify,
    AlertType? Type,
    decimal? Threshold);

/// <summary>
/// A regra de alerta, isolada e sem efeitos colaterais: entra preço + estado,
/// sai uma decisão. Não toca em banco, não manda e-mail, não olha o relógio
/// sozinho — por isso dá para testar sem internet e sem esperar o mercado mexer.
///
/// É a mesma regra do StockMonitor da Etapa 1, agora aplicada por inscrição:
/// só avisa quando o preço MUDA de faixa, e o cooldown reenvia um lembrete
/// depois de um tempo mesmo sem mudar.
/// </summary>
public static class AlertEngine
{
    public static AlertDecision Evaluate(
        Subscription subscription,
        decimal price,
        decimal buyThreshold,
        decimal sellThreshold,
        DateTime now,
        TimeSpan cooldown)
    {
        if (sellThreshold <= buyThreshold)
            throw new ArgumentException(
                "O limite de venda deve ser maior que o de compra.", nameof(sellThreshold));

        Zone zone = ClassifyZone(price, buyThreshold, sellThreshold);

        // Na faixa neutra não se avisa nada. Guardar a zona é o que permite
        // detectar o próximo cruzamento quando o preço sair dela.
        if (zone == Zone.Neutra)
            return new AlertDecision(zone, ShouldNotify: false, Type: null, Threshold: null);

        // Mudou de faixa desde a última vez? Esse é o "cruzamento".
        bool crossed = subscription.LastZone != zone;

        // Mesmo sem cruzar, reenvia um lembrete quando o cooldown vence.
        DateTime? lastNotice = zone == Zone.Venda
            ? subscription.LastSellNoticeAt
            : subscription.LastBuyNoticeAt;

        bool cooledDown = lastNotice is null || now - lastNotice.Value >= cooldown;

        bool notify = crossed || cooledDown;

        return new AlertDecision(
            zone,
            notify,
            notify ? zone.ToAlertType() : null,
            notify ? (zone == Zone.Venda ? sellThreshold : buyThreshold) : null);
    }

    /// <summary>
    /// Preço exatamente igual a um limite conta como neutro: só é "caro" quando
    /// passa da linha de venda, e "barato" quando fica abaixo da de compra.
    /// Mesmo critério da Etapa 1 (&gt; e &lt;, nunca &gt;= ou &lt;=).
    /// </summary>
    public static Zone ClassifyZone(decimal price, decimal buyThreshold, decimal sellThreshold)
    {
        if (price > sellThreshold) return Zone.Venda;
        if (price < buyThreshold) return Zone.Compra;
        return Zone.Neutra;
    }

    /// <summary>
    /// Aplica a decisão ao estado da inscrição, depois que o e-mail foi enviado.
    ///
    /// Só se chama isto quando o envio deu certo. Se o e-mail falhar, o estado
    /// fica como estava e a próxima rodada tenta de novo — igual à Etapa 1.
    /// </summary>
    public static Subscription ApplyNotified(Subscription subscription, Zone zone, DateTime sentAt) =>
        subscription with
        {
            LastZone = zone,
            LastSellNoticeAt = zone == Zone.Venda ? sentAt : subscription.LastSellNoticeAt,
            LastBuyNoticeAt = zone == Zone.Compra ? sentAt : subscription.LastBuyNoticeAt
        };

    /// <summary>Atualiza só a faixa, quando não houve envio (faixa neutra).</summary>
    public static Subscription ApplyZoneOnly(Subscription subscription, Zone zone) =>
        subscription with { LastZone = zone };
}
