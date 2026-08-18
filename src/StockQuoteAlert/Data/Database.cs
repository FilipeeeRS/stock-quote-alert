using Microsoft.Data.Sqlite;

namespace StockQuoteAlert.Data;

/// <summary>
/// Abre conexões com o banco e garante que as tabelas existam.
///
/// Usamos SQL escrito à mão (em vez de um ORM) por dois motivos:
/// 1) você enxerga exatamente o que vai para o banco, sem mágica;
/// 2) trocar SQLite por Postgres mais tarde vira mexer neste arquivo, não no sistema todo.
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string filePath)
    {
        FilePath = ResolverCaminho(filePath);

        // Foreign Keys=True faz o SQLite realmente cobrar os relacionamentos
        // (por padrão ele aceita um InscricaoId que não existe).
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            ForeignKeys = true
        }.ToString();
    }

    /// <summary>
    /// Transforma o caminho do config.json num caminho absoluto, ancorado na
    /// raiz do projeto (a pasta que tem o arquivo .sln).
    ///
    /// POR QUE ISSO EXISTE: 'data/alerts.db' é relativo à pasta de onde o
    /// programa roda, e o 'dotnet run' usa a pasta de cada projeto. Sem ancorar,
    /// o console gravava num banco e o site noutro — você cadastrava pela linha
    /// de comando e o site não mostrava nada, sem nenhuma pista do motivo.
    ///
    /// Caminho absoluto no config é respeitado como está. E quando o programa
    /// for publicado não existe .sln nenhum, então cai na pasta de execução,
    /// que é o esperado num servidor.
    /// </summary>
    private static string ResolverCaminho(string caminhoConfigurado)
    {
        if (Path.IsPathRooted(caminhoConfigurado))
            return caminhoConfigurado;

        string ancora = EncontrarRaizDoProjeto() ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(ancora, caminhoConfigurado));
    }

    private static string? EncontrarRaizDoProjeto()
    {
        // Sobe a partir da pasta do executável procurando o .sln.
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);

        while (pasta is not null)
        {
            if (pasta.GetFiles("*.sln").Length > 0)
                return pasta.FullName;

            pasta = pasta.Parent;
        }

        return null;
    }

    public string FilePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Cria as tabelas se ainda não existirem.
    ///
    /// A tabela SchemaVersao guarda em que "versão" o banco está. Assim, quando
    /// precisarmos mudar o formato mais adiante, dá para aplicar só o que falta,
    /// sem apagar os dados de quem já usa o sistema.
    /// </summary>
    public void EnsureCreated()
    {
        // Garante que a pasta do arquivo exista (ex.: "data/alerts.db").
        string? folder = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        using var connection = Open();

        Execute(connection, "CREATE TABLE IF NOT EXISTS SchemaVersao (Versao INTEGER NOT NULL);");

        int current = CurrentVersion(connection);

        if (current < 1)
        {
            ApplyVersion1(connection);
            SetVersion(connection, 1);
        }

        if (current < 2)
        {
            ApplyVersion2(connection);
            SetVersion(connection, 2);
        }
    }

    /// <summary>
    /// Guarda quais percentis geraram cada limite.
    ///
    /// POR QUE: os limites ficam guardados por 24h para não refazer a conta a
    /// cada rodada. Só que, sem registrar de onde vieram, mudar 'buyPercentile'
    /// ou 'sellPercentile' no config.json não surtia efeito nenhum até o prazo
    /// vencer — e sem nenhuma mensagem explicando. Agora, se a configuração
    /// mudar, o limite é recalculado na rodada seguinte.
    ///
    /// É também a primeira migração de verdade: quem já tem banco criado ganha
    /// as colunas novas sem perder as inscrições.
    /// </summary>
    private static void ApplyVersion2(SqliteConnection connection)
    {
        foreach (string coluna in new[] { "PercentilCompra", "PercentilVenda" })
        {
            if (!ColunaExiste(connection, "Ativos", coluna))
                Execute(connection, $"ALTER TABLE Ativos ADD COLUMN {coluna} INTEGER NULL;");
        }
    }

    private static bool ColunaExiste(SqliteConnection connection, string tabela, string coluna)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tabela});";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), coluna, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void ApplyVersion1(SqliteConnection connection)
    {
        Execute(connection, """
            -- Cada linha é "este e-mail quer ser avisado sobre este ativo".
            -- Note que não existe tabela de usuários: não há cadastro nem senha.
            CREATE TABLE IF NOT EXISTS Inscricoes (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                Ticker              TEXT    NOT NULL,
                Email               TEXT    NOT NULL,

                -- Código secreto e aleatório usado pelo botão "Cancelar" do e-mail.
                -- Precisa ser imprevisível: se fosse o próprio Id, qualquer pessoa
                -- trocaria o número na URL e cancelaria a inscrição de um estranho.
                TokenCancelamento   TEXT    NOT NULL UNIQUE,

                Ativa               INTEGER NOT NULL DEFAULT 1,
                CriadaEm            TEXT    NOT NULL,
                CanceladaEm         TEXT    NULL,

                -- Estado do alerta. Antes isto vivia na memória do StockMonitor
                -- e sumia quando o programa fechava. Agora sobrevive ao reinício.
                UltimaZona          TEXT    NULL,
                UltimoAvisoCompraEm TEXT    NULL,
                UltimoAvisoVendaEm  TEXT    NULL
            );
            """);

        // O worker pergunta "quais inscrições ativas existem para este ativo?"
        // a cada rodada. Sem índice, o banco leria a tabela inteira toda vez.
        Execute(connection,
            "CREATE INDEX IF NOT EXISTS IX_Inscricoes_Ativa_Ticker ON Inscricoes (Ativa, Ticker);");

        // Impede a mesma pessoa de se inscrever duas vezes no mesmo ativo
        // (e receber e-mail dobrado). Vale só para inscrições ativas: se ela
        // cancelar e quiser voltar depois, consegue.
        Execute(connection, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Inscricoes_Email_Ticker
                ON Inscricoes (Email, Ticker) WHERE Ativa = 1;
            """);

        Execute(connection, """
            -- Uma linha por ativo, compartilhada por todos os inscritos nele.
            -- É esta tabela que economiza sua cota da API: 50 pessoas acompanhando
            -- PETR4 custam UMA consulta, não 50.
            --
            -- Valores em dinheiro são guardados como TEXTO ("41.21"), nunca como
            -- número de ponto flutuante. float/double não conseguem representar
            -- 0,10 exatamente — guardam algo como 0,09999999 — e em conta de
            -- dinheiro isso vira erro.
            CREATE TABLE IF NOT EXISTS Ativos (
                Ticker              TEXT PRIMARY KEY,
                PrecoAtual          TEXT NULL,
                LimiteCompra        TEXT NULL,
                LimiteVenda         TEXT NULL,
                LimitesCalculadosEm TEXT NULL,
                ConsultadoEm        TEXT NULL
            );
            """);

        Execute(connection, """
            -- Histórico do que já foi enviado. Serve para você conferir se o
            -- sistema funcionou e, mais adiante, para mostrar o histórico ao usuário.
            CREATE TABLE IF NOT EXISTS Avisos (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                InscricaoId INTEGER NOT NULL REFERENCES Inscricoes(Id),
                Tipo        TEXT    NOT NULL,
                Preco       TEXT    NOT NULL,
                Limite      TEXT    NOT NULL,
                EnviadoEm   TEXT    NOT NULL
            );
            """);

        Execute(connection,
            "CREATE INDEX IF NOT EXISTS IX_Avisos_Inscricao ON Avisos (InscricaoId, EnviadoEm);");
    }

    private static int CurrentVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Versao), 0) FROM SchemaVersao;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SetVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO SchemaVersao (Versao) VALUES ($v);";
        command.Parameters.AddWithValue("$v", version);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
