using Npgsql;

namespace RagMini;

/// <summary>Genel metrik özeti (query_log tablosundan agregat).</summary>
public sealed record MetricsSnapshot(
    long Questions, long CacheHits, long LlmCalls,
    double CacheHitRate, double AvgLatencyMs, long InputTokens, long OutputTokens);

/// <summary>
/// Her soruyu query_log tablosuna yazar ve genel metrikleri DB'den hesaplar.
/// Metrikler KALICIDIR: uygulama kapansa bile geçmiş istekler durur.
/// </summary>
public sealed class MetricsStore
{
    private readonly NpgsqlDataSource _db;
    public MetricsStore(NpgsqlDataSource db) => _db = db;

    public async Task LogAsync(string question, bool fromCache, long latencyMs,
                               long inputTokens, long outputTokens, string sources)
    {
        await using var cmd = _db.CreateCommand(
            "INSERT INTO query_log (question, from_cache, latency_ms, input_tokens, output_tokens, sources) " +
            "VALUES ($1, $2, $3, $4, $5, $6)");
        cmd.Parameters.AddWithValue(question);
        cmd.Parameters.AddWithValue(fromCache);
        cmd.Parameters.AddWithValue((int)latencyMs);
        cmd.Parameters.AddWithValue((int)inputTokens);
        cmd.Parameters.AddWithValue((int)outputTokens);
        cmd.Parameters.AddWithValue(sources);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<MetricsSnapshot> SnapshotAsync()
    {
        await using var cmd = _db.CreateCommand("""
            SELECT
                count(*),
                count(*) FILTER (WHERE from_cache),
                count(*) FILTER (WHERE NOT from_cache),
                COALESCE(round(avg(latency_ms))::float8, 0),
                COALESCE(sum(input_tokens), 0),
                COALESCE(sum(output_tokens), 0)
            FROM query_log
            """);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();

        long q = r.GetInt64(0), hits = r.GetInt64(1), llm = r.GetInt64(2);
        double avg = r.GetDouble(3);
        long inTok = r.GetInt64(4), outTok = r.GetInt64(5);
        double hitRate = q == 0 ? 0 : Math.Round(100.0 * hits / q, 1);

        return new MetricsSnapshot(q, hits, llm, hitRate, avg, inTok, outTok);
    }

    public async Task<string> SummaryAsync()
    {
        var s = await SnapshotAsync();
        return $"""
            ── 📈 Metrikler (DB: query_log) ──
               Soru sayısı        : {s.Questions}
               Önbellek isabeti   : {s.CacheHits} ({s.CacheHitRate:F1}%)
               LLM çağrısı        : {s.LlmCalls}
               Ortalama gecikme   : {s.AvgLatencyMs:F0} ms
               Token (giriş/çıkış): {s.InputTokens} / {s.OutputTokens}
            """;
    }
}
