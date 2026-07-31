using Microsoft.Data.Sqlite;
using StockQuoteAlert.Alerts;

namespace StockQuoteAlert.Data;

/// <summary>Leitura e escrita da tabela Inscricoes.</summary>
public sealed class SubscriptionRepository
{
    private const string Columns =
        "Id, Ticker, Email, TokenCancelamento, Ativa, CriadaEm, CanceladaEm, " +
        "UltimaZona, UltimoAvisoCompraEm, UltimoAvisoVendaEm";

    private readonly Database _db;

    public SubscriptionRepository(Database db) => _db = db;

    /// <summary>
    /// Cria uma inscrição. Devolve null se este e-mail já acompanha este ativo
    /// (o índice único do banco barra a duplicata — é o banco que garante a regra,
    /// não o código, então nem uma corrida entre dois cliques simultâneos passa).
    /// </summary>
    public Subscription? Add(string ticker, string email, DateTime createdAt)
    {
        string token = Guid.NewGuid().ToString("N");

        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Inscricoes (Ticker, Email, TokenCancelamento, Ativa, CriadaEm)
            VALUES ($ticker, $email, $token, 1, $criadaEm)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$token", token);
        command.Parameters.AddWithValue("$criadaEm", DbValue.FromDate(createdAt));

        try
        {
            long id = Convert.ToInt64(command.ExecuteScalar());
            return new Subscription(id, ticker, email, token, true, createdAt,
                null, null, null, null);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            // 19 = violação de restrição (o índice único de e-mail + ativo).
            return null;
        }
    }

    public IReadOnlyList<Subscription> ListActiveByTicker(string ticker)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {Columns} FROM Inscricoes WHERE Ativa = 1 AND Ticker = $ticker ORDER BY Id;";
        command.Parameters.AddWithValue("$ticker", ticker);
        return Read(command);
    }

    public IReadOnlyList<Subscription> ListAll()
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM Inscricoes ORDER BY Id;";
        return Read(command);
    }

    /// <summary>
    /// Os ativos distintos que precisam ser consultados nesta rodada.
    /// É esta consulta que evita chamar a API uma vez por inscrito.
    /// </summary>
    public IReadOnlyList<string> DistinctActiveTickers()
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT DISTINCT Ticker FROM Inscricoes WHERE Ativa = 1 ORDER BY Ticker;";

        var tickers = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            tickers.Add(reader.GetString(0));
        return tickers;
    }

    /// <summary>Cancela pelo token do botão do e-mail. Devolve o e-mail cancelado, ou null.</summary>
    public string? CancelByToken(string token, DateTime cancelledAt)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Inscricoes
               SET Ativa = 0, CanceladaEm = $agora
             WHERE TokenCancelamento = $token AND Ativa = 1
            RETURNING Email;
            """;
        command.Parameters.AddWithValue("$token", token);
        command.Parameters.AddWithValue("$agora", DbValue.FromDate(cancelledAt));

        object? result = command.ExecuteScalar();
        return result is null or DBNull ? null : (string)result;
    }

    /// <summary>Grava o estado do alerta depois de avaliar um preço.</summary>
    public void SaveAlertState(Subscription subscription)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Inscricoes
               SET UltimaZona = $zona,
                   UltimoAvisoCompraEm = $compra,
                   UltimoAvisoVendaEm = $venda
             WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", subscription.Id);
        command.Parameters.AddWithValue("$zona",
            (object?)subscription.LastZone?.ToDb() ?? DBNull.Value);
        command.Parameters.AddWithValue("$compra",
            (object?)DbValue.FromDate(subscription.LastBuyNoticeAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$venda",
            (object?)DbValue.FromDate(subscription.LastSellNoticeAt) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static List<Subscription> Read(SqliteCommand command)
    {
        var list = new List<Subscription>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Subscription(
                Id: reader.GetInt64(0),
                Ticker: reader.GetString(1),
                Email: reader.GetString(2),
                CancelToken: reader.GetString(3),
                Active: reader.GetInt64(4) == 1,
                CreatedAt: DbValue.ToDate(reader.GetString(5)),
                CancelledAt: DbValue.ToDateOrNull(reader.GetValue(6)),
                LastZone: ZoneExtensions.FromDb(DbValue.ToStringOrNull(reader.GetValue(7))),
                LastBuyNoticeAt: DbValue.ToDateOrNull(reader.GetValue(8)),
                LastSellNoticeAt: DbValue.ToDateOrNull(reader.GetValue(9))));
        }
        return list;
    }
}
