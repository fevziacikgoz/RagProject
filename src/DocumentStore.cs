using Npgsql;
using Pgvector;

namespace RagMini;

/// <summary>docs tablosu: artımlı indexleme işlemleri + hybrid aday getirme.</summary>
public sealed class DocumentStore
{
    private readonly NpgsqlDataSource _db;
    public DocumentStore(NpgsqlDataSource db) => _db = db;

    public async Task<string?> GetHashAsync(string source)
    {
        await using var cmd = _db.CreateCommand("SELECT content_hash FROM docs WHERE source = $1 LIMIT 1");
        cmd.Parameters.AddWithValue(source);
        return await cmd.ExecuteScalarAsync() as string;
    }

    public async Task DeleteAsync(string source)
    {
        await using var cmd = _db.CreateCommand("DELETE FROM docs WHERE source = $1");
        cmd.Parameters.AddWithValue(source);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InsertAsync(string source, string content, float[] embedding, string hash)
    {
        await using var cmd = _db.CreateCommand(
            "INSERT INTO docs (source, content, embedding, content_hash) VALUES ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(source);
        cmd.Parameters.AddWithValue(content);
        cmd.Parameters.AddWithValue(new Vector(embedding));
        cmd.Parameters.AddWithValue(hash);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> AllSourcesAsync()
    {
        var list = new List<string>();
        await using var cmd = _db.CreateCommand("SELECT DISTINCT source FROM docs");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>
    /// HYBRID RETRIEVAL: iki aday kümesini birleştirir —
    /// (a) vektör (anlamsal) en yakınlar, (b) kelime (pg_trgm) en benzerler.
    /// Böylece typo / nadir kelimeleri de yakalar. Re-rank C# tarafında yapılır.
    /// </summary>
    public async Task<List<RetrievedChunk>> RetrieveCandidatesAsync(float[] qVector, string questionText, int limit)
    {
        var byContent = new Dictionary<string, RetrievedChunk>();

        await using var cmd = _db.CreateCommand($"""
            (SELECT source, content, embedding <=> $1 AS vdist, similarity(content, $2)::float8 AS lex
             FROM docs ORDER BY embedding <=> $1 LIMIT {limit})
            UNION
            (SELECT source, content, embedding <=> $1 AS vdist, similarity(content, $2)::float8 AS lex
             FROM docs ORDER BY similarity(content, $2) DESC LIMIT {limit})
            """);
        cmd.Parameters.AddWithValue(new Vector(qVector));
        cmd.Parameters.AddWithValue(questionText);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var chunk = new RetrievedChunk(r.GetString(0), r.GetString(1), r.GetDouble(2), r.GetDouble(3));
            byContent[chunk.Content] = chunk; // aynı parça iki kümede ise tekille
        }
        return byContent.Values.ToList();
    }
}
