using StockQuoteAlert.Cli;
using Xunit;

namespace StockQuoteAlert.Tests;

public class CliArgumentsTests
{
    [Fact]
    public void Parses_valid_arguments()
    {
        bool ok = CliArguments.TryParse(
            new[] { "petr4", "22.67", "22.59" }, out var result, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("PETR4", result!.Ticker);          // normaliza para maiúsculas
        Assert.Equal(22.67m, result.SellThreshold);
        Assert.Equal(22.59m, result.BuyThreshold);
    }

    [Fact]
    public void Accepts_comma_as_decimal_separator()
    {
        bool ok = CliArguments.TryParse(
            new[] { "VALE3", "60,50", "58,10" }, out var result, out _);

        Assert.True(ok);
        Assert.Equal(60.50m, result!.SellThreshold);
        Assert.Equal(58.10m, result.BuyThreshold);
    }

    [Theory]
    [InlineData(new[] { "PETR4", "22.67" })]                 // poucos args
    [InlineData(new[] { "PETR4", "22.67", "22.59", "x" })]   // args demais
    public void Rejects_wrong_arg_count(string[] args)
    {
        bool ok = CliArguments.TryParse(args, out var result, out var error);
        Assert.False(ok);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_non_numeric_price()
    {
        bool ok = CliArguments.TryParse(
            new[] { "PETR4", "abc", "22.59" }, out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_sell_not_greater_than_buy()
    {
        bool ok = CliArguments.TryParse(
            new[] { "PETR4", "22.00", "22.59" }, out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
    }
}
