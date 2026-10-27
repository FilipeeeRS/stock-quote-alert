# Stock Quote Alert

C# (.NET 8) app that monitors B3 stock quotes and sends email alerts when a price looks good to buy or sell. Quotes come from the public [brapi.dev](https://brapi.dev) API.

## Modes

| Mode | Description |
| --- | --- |
| **Web** | Page where users enter a ticker and an email to subscribe, no account needed |
| **Subscriptions** | Same features through the command line |
| **Original** | Monitors a single ticker with buy/sell thresholds passed as arguments |

## Running

**Web** (the background worker runs in the same process):

```bash
dotnet run --project src/StockQuoteAlert.Web
```

Then open <http://localhost:5000>.

**Original:**

```bash
dotnet run --project src/StockQuoteAlert -- PETR4 22.67 22.59
```

**Subscriptions:**

```bash
dotnet run --project src/StockQuoteAlert -- inscrever PETR4 you@email.com   # subscribe
dotnet run --project src/StockQuoteAlert -- listar                          # list subscriptions
dotnet run --project src/StockQuoteAlert -- monitorar [--uma-vez]           # run worker (once)
dotnet run --project src/StockQuoteAlert -- cancelar <token>                # cancel
```

## How thresholds work

Instead of fixed prices, the app uses the last 6 months of closing prices for each ticker:

- Below the 20th percentile: buy signal
- Above the 80th percentile: sell signal
- In between: no alert

Thresholds adapt as the stock's price level changes and are recalculated every 24 hours.

> This is not investment advice.

## Configuration

```bash
cp src/StockQuoteAlert/config.example.json src/StockQuoteAlert/config.json
```

Fill in `config.json` (each field is documented in the example file). For Gmail, use an app password. `config.json` and `data/` are git-ignored.

Without an API token, brapi.dev only serves PETR4, VALE3, ITUB4 and MGLU3. The free plan has a monthly request limit and quotes are delayed by about 30 minutes.

## Tests

```bash
dotnet test
```

61 tests covering input validation, threshold calculation, alert rules and the full worker cycle, with no network or real emails.

## Design notes

- Alerts fire when the price crosses into a new range, with a configurable cooldown, so users don't get repeated emails
- Alert state is stored in SQLite, so restarting the worker doesn't resend alerts
- One API call per ticker, no matter how many subscribers
- Quote lookups are cached for 5 minutes to protect the API quota
- The worker sleeps outside trading hours
- Network or email failures are logged and retried on the next cycle
- Quote and notification services sit behind interfaces, making them testable and swappable
- Money is stored as invariant text, never as floating point, and dates are stored in UTC
- The cancel link asks for confirmation, since email scanners often open links automatically
