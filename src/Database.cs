using Npgsql;

namespace RagMini;

/// <summary>NpgsqlDataSource kurulumu + şema (tablolar/extension'lar) garantisi.</summary>
public sealed class Database
{
    public NpgsqlDataSource DataSource { get; }
    private Database(NpgsqlDataSource ds) => DataSource = ds;

    public static async Task<Database> CreateAsync(string connString)
    {
        var builder = new NpgsqlDataSourceBuilder(connString);
        builder.UseVector(); // pgvector "vector" tipini Npgsql'e tanıt
        var db = new Database(builder.Build());
        await db.EnsureSchemaAsync();
        return db;
    }

    private async Task EnsureSchemaAsync()
    {
        await using var cmd = DataSource.CreateCommand($"""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE EXTENSION IF NOT EXISTS pg_trgm;   -- hybrid arama için kelime benzerliği

            CREATE TABLE IF NOT EXISTS docs (
                id           SERIAL PRIMARY KEY,
                source       TEXT,
                content      TEXT,
                embedding    VECTOR({RagOptions.EmbeddingDimensions}),
                content_hash TEXT
            );

            CREATE TABLE IF NOT EXISTS qa_cache (
                id         SERIAL PRIMARY KEY,
                question   TEXT,
                embedding  VECTOR({RagOptions.EmbeddingDimensions}),
                answer     TEXT,
                sources    TEXT,
                created_at TIMESTAMPTZ DEFAULT now()
            );
            """);
        await cmd.ExecuteNonQueryAsync();
    }
}
