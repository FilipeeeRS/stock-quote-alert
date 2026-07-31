using StockQuoteAlert.Alerts;
using StockQuoteAlert.Notifications;
using StockQuoteAlert.Quotes;

namespace StockQuoteAlert.Tests;

/// <summary>Provedor de cotações falso: devolve o que mandarmos, sem tocar na rede.</summary>
internal sealed class FakeQuoteProvider : IQuoteProvider
{
    public decimal? Next { get; set; }

    /// <summary>Preço e histórico por ativo. Ativo ausente = falha de consulta (null).</summary>
    public Dictionary<string, QuoteSnapshot> Snapshots { get; } = new();

    /// <summary>Quantas vezes a API foi chamada — é assim que provamos a deduplicação.</summary>
    public int SnapshotCalls { get; private set; }

    public Task<decimal?> GetCurrentPriceAsync(string ticker, CancellationToken ct)
        => Task.FromResult(Next);

    public Task<QuoteSnapshot?> GetSnapshotAsync(string ticker, string range, CancellationToken ct)
    {
        SnapshotCalls++;
        return Task.FromResult(Snapshots.TryGetValue(ticker, out var snapshot) ? snapshot : null);
    }

    public void Set(string ticker, decimal price, IReadOnlyList<decimal> history)
        => Snapshots[ticker] = new QuoteSnapshot(ticker, price, history);
}

/// <summary>Notificador que apenas registra as chamadas, sem enviar e-mail (Etapa 1).</summary>
internal sealed class RecordingNotifier : INotifier
{
    public List<(AlertType Type, decimal Price, decimal Threshold)> Sent { get; } = new();

    public Task NotifyAsync(AlertType type, string ticker, decimal currentPrice,
        decimal threshold, CancellationToken ct)
    {
        Sent.Add((type, currentPrice, threshold));
        return Task.CompletedTask;
    }
}

/// <summary>Notificador de inscritos que registra os envios (Etapa 2).</summary>
internal sealed class RecordingSubscriberNotifier : ISubscriberNotifier
{
    public List<SubscriberAlert> Sent { get; } = new();

    /// <summary>Quando preenchido, o envio para este e-mail falha — para testar resiliência.</summary>
    public string? FailFor { get; set; }

    public Task NotifyAsync(SubscriberAlert alert, CancellationToken ct)
    {
        if (FailFor is not null && alert.Email == FailFor)
            throw new InvalidOperationException("falha simulada de SMTP");

        Sent.Add(alert);
        return Task.CompletedTask;
    }
}
