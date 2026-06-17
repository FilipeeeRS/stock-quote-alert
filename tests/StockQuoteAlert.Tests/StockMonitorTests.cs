using StockQuoteAlert.Alerts;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Configuration;
using StockQuoteAlert.Monitoring;
using Xunit;

namespace StockQuoteAlert.Tests;

public class StockMonitorTests
{
    private static readonly CliArguments Args = new("PETR4", SellThreshold: 22.67m, BuyThreshold: 22.59m);

    private static (StockMonitor monitor, RecordingNotifier notifier, Func<DateTime> _) Build(
        DateTime start, int cooldownMinutes = 15)
    {
        var clockTime = start;
        Func<DateTime> clock = () => clockTime;

        var notifier = new RecordingNotifier();
        var settings = new AppSettings
        {
            PollIntervalSeconds = 1,
            AlertCooldownMinutes = cooldownMinutes
        };

        var monitor = new StockMonitor(new FakeQuoteProvider(), notifier, Args, settings, clock);
        return (monitor, notifier, clock);
    }

    [Fact]
    public async Task Alerts_sell_when_price_above_blue_line()
    {
        var (monitor, notifier, _) = Build(DateTime.UtcNow);
        await monitor.ProcessPriceAsync(22.80m, default);

        var alert = Assert.Single(notifier.Sent);
        Assert.Equal(AlertType.Sell, alert.Type);
    }

    [Fact]
    public async Task Alerts_buy_when_price_below_red_line()
    {
        var (monitor, notifier, _) = Build(DateTime.UtcNow);
        await monitor.ProcessPriceAsync(22.50m, default);

        var alert = Assert.Single(notifier.Sent);
        Assert.Equal(AlertType.Buy, alert.Type);
    }

    [Fact]
    public async Task Does_not_alert_inside_neutral_band()
    {
        var (monitor, notifier, _) = Build(DateTime.UtcNow);
        await monitor.ProcessPriceAsync(22.60m, default);

        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task Does_not_repeat_alert_while_staying_in_same_zone()
    {
        var (monitor, notifier, _) = Build(DateTime.UtcNow);

        await monitor.ProcessPriceAsync(22.80m, default); // cruza para cima -> alerta
        await monitor.ProcessPriceAsync(22.85m, default); // continua acima -> sem novo alerta
        await monitor.ProcessPriceAsync(22.90m, default); // idem

        Assert.Single(notifier.Sent);
    }

    [Fact]
    public async Task Re_alerts_after_leaving_and_re_crossing()
    {
        var (monitor, notifier, _) = Build(DateTime.UtcNow);

        await monitor.ProcessPriceAsync(22.80m, default); // venda
        await monitor.ProcessPriceAsync(22.60m, default); // faixa neutra (reseta zona)
        await monitor.ProcessPriceAsync(22.80m, default); // novo cruzamento -> novo alerta

        Assert.Equal(2, notifier.Sent.Count);
        Assert.All(notifier.Sent, s => Assert.Equal(AlertType.Sell, s.Type));
    }
}
