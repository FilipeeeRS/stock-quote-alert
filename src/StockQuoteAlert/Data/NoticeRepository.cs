using StockQuoteAlert.Alerts;

namespace StockQuoteAlert.Data;

/// <summary>Registro dos avisos já enviados (tabela Avisos).</summary>
public sealed class NoticeRepository
{
    private readonly Database _db;

    public NoticeRepository(Database db) => _db = db;

    public void Add(long subscriptionId, AlertType type, decimal price,
        decimal threshold, DateTime sentAt)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Avisos (InscricaoId, Tipo, Preco, Limite, EnviadoEm)
            VALUES ($inscricao, $tipo, $preco, $limite, $enviadoEm);
            """;
        command.Parameters.AddWithValue("$inscricao", subscriptionId);
        command.Parameters.AddWithValue("$tipo", type == AlertType.Sell ? "VENDA" : "COMPRA");
        command.Parameters.AddWithValue("$preco", DbValue.FromMoney(price));
        command.Parameters.AddWithValue("$limite", DbValue.FromMoney(threshold));
        command.Parameters.AddWithValue("$enviadoEm", DbValue.FromDate(sentAt));
        command.ExecuteNonQuery();
    }

    public int CountBySubscription(long subscriptionId)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Avisos WHERE InscricaoId = $id;";
        command.Parameters.AddWithValue("$id", subscriptionId);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
