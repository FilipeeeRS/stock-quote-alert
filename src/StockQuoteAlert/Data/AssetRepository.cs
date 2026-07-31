namespace StockQuoteAlert.Data;

/// <summary>Leitura e escrita da tabela Ativos (cotação e limites por ativo).</summary>
public sealed class AssetRepository
{
    private readonly Database _db;

    public AssetRepository(Database db) => _db = db;

    public Asset? Get(string ticker)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Ticker, PrecoAtual, LimiteCompra, LimiteVenda,
                   LimitesCalculadosEm, ConsultadoEm
              FROM Ativos WHERE Ticker = $ticker;
            """;
        command.Parameters.AddWithValue("$ticker", ticker);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new Asset(
            Ticker: reader.GetString(0),
            CurrentPrice: DbValue.ToMoneyOrNull(reader.GetValue(1)),
            BuyThreshold: DbValue.ToMoneyOrNull(reader.GetValue(2)),
            SellThreshold: DbValue.ToMoneyOrNull(reader.GetValue(3)),
            ThresholdsComputedAt: DbValue.ToDateOrNull(reader.GetValue(4)),
            CheckedAt: DbValue.ToDateOrNull(reader.GetValue(5)));
    }

    /// <summary>
    /// Insere ou atualiza o ativo (o "upsert" do SQLite: tenta inserir e,
    /// se o ticker já existir, atualiza em vez de dar erro).
    /// </summary>
    public void Save(Asset asset)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Ativos (Ticker, PrecoAtual, LimiteCompra, LimiteVenda,
                                LimitesCalculadosEm, ConsultadoEm)
            VALUES ($ticker, $preco, $compra, $venda, $calculadosEm, $consultadoEm)
            ON CONFLICT(Ticker) DO UPDATE SET
                PrecoAtual          = excluded.PrecoAtual,
                LimiteCompra        = excluded.LimiteCompra,
                LimiteVenda         = excluded.LimiteVenda,
                LimitesCalculadosEm = excluded.LimitesCalculadosEm,
                ConsultadoEm        = excluded.ConsultadoEm;
            """;
        command.Parameters.AddWithValue("$ticker", asset.Ticker);
        command.Parameters.AddWithValue("$preco",
            (object?)DbValue.FromMoney(asset.CurrentPrice) ?? DBNull.Value);
        command.Parameters.AddWithValue("$compra",
            (object?)DbValue.FromMoney(asset.BuyThreshold) ?? DBNull.Value);
        command.Parameters.AddWithValue("$venda",
            (object?)DbValue.FromMoney(asset.SellThreshold) ?? DBNull.Value);
        command.Parameters.AddWithValue("$calculadosEm",
            (object?)DbValue.FromDate(asset.ThresholdsComputedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$consultadoEm",
            (object?)DbValue.FromDate(asset.CheckedAt) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
