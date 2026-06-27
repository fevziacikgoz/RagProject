using Npgsql;
using Pgvector;

namespace RagMini;

/// <summary>qa_cache tablosu: semantic cache okuma/yazma/temizleme.</summary>
public sealed class CacheStore
{
    private readonly NpgsqlDataSource _db;
    public CacheStore(NpgsqlDataSource db) => _db = db;

    public async Task<CacheHit?> LookupAsync(float[] qVector)
    {
        await using var cmd = _db.CreateCommand(
            "SELECT answer, sources, embedding <=> $1 AS dist FROM qa_cache ORDER BY embedding <=> $1 LIMIT 1");
        cmd.Parameters.AddWithValue(new Vector(qVector));
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            return new CacheHit(r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetDouble(2));
        return null;
    }

    public async Task SaveAsync(string question, float[] qVector, string answer, string sources)
    {
        await using var cmd = _db.CreateCommand(
            "INSERT INTO qa_cache (question, embedding, answer, sources) VALUES ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(question);
        cmd.Parameters.AddWithValue(new Vector(qVector));
        cmd.Parameters.AddWithValue(answer);
        cmd.Parameters.AddWithValue(sources);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> ClearAsync()
    {
        await using var cmd = _db.CreateCommand("DELETE FROM qa_cache");
        return await cmd.ExecuteNonQueryAsync();
    }
}
