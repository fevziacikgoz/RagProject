using System.Diagnostics;
using System.Text;

namespace RagMini;

/// <summary>
/// Çekirdek akış: soru → (semantic cache) → hybrid retrieve + re-rank →
/// augmented prompt → LLM → cache'e yaz. Her soruyu query_log'a metrik olarak yazar.
/// </summary>
public sealed class RagPipeline
{
    private const string SystemPrompt = "Sen bir İK asistanısın. Sadece verilen bağlamı kullan.";

    private readonly EmbeddingService _embedder;
    private readonly ChatService _chat;
    private readonly HybridRetriever _retriever;
    private readonly CacheStore _cache;
    private readonly AdaptiveThreshold _threshold;
    private readonly MetricsStore _metrics;

    public RagPipeline(EmbeddingService embedder, ChatService chat, HybridRetriever retriever,
                       CacheStore cache, AdaptiveThreshold threshold, MetricsStore metrics)
    {
        _embedder = embedder;
        _chat = chat;
        _retriever = retriever;
        _cache = cache;
        _threshold = threshold;
        _metrics = metrics;
    }

    /// <summary>Tek seferde cevap (eval / web JSON için). Token + gecikme metriği DB'ye yazılır.</summary>
    public async Task<AnswerResult> AskAsync(string question)
    {
        var sw = Stopwatch.StartNew();
        float[] qVector = await _embedder.EmbedAsync(question);

        if (await TryCacheAsync(qVector) is { } cached)
        {
            await _metrics.LogAsync(question, true, sw.ElapsedMilliseconds, 0, 0, Join(cached.Sources));
            return cached;
        }

        var chunks = await _retriever.RetrieveAsync(qVector, question);
        var (answer, inTokens, outTokens) = await _chat.AnswerAsync(SystemPrompt, BuildPrompt(question, chunks));
        var result = await PersistAsync(question, qVector, answer, chunks);
        await _metrics.LogAsync(question, false, sw.ElapsedMilliseconds, inTokens, outTokens, Join(result.Sources));
        return result;
    }

    /// <summary>Streaming cevap: her token üretildikçe onToken (async) çağrılır.</summary>
    public async Task<AnswerResult> AskStreamingAsync(string question, Func<string, Task> onToken)
    {
        var sw = Stopwatch.StartNew();
        float[] qVector = await _embedder.EmbedAsync(question);

        if (await TryCacheAsync(qVector) is { } cached)
        {
            await onToken(cached.Answer);
            await _metrics.LogAsync(question, true, sw.ElapsedMilliseconds, 0, 0, Join(cached.Sources));
            return cached;
        }

        var chunks = await _retriever.RetrieveAsync(qVector, question);
        var sb = new StringBuilder();
        await foreach (var token in _chat.AnswerStreamingAsync(SystemPrompt, BuildPrompt(question, chunks)))
        {
            sb.Append(token);
            await onToken(token);
        }
        var result = await PersistAsync(question, qVector, sb.ToString(), chunks);
        await _metrics.LogAsync(question, false, sw.ElapsedMilliseconds, 0, 0, Join(result.Sources)); // streaming'de token usage gelmez
        return result;
    }

    public string? TickThreshold() => _threshold.Tick();

    // ── yardımcılar ──────────────────────────────────────────────────
    private async Task<AnswerResult?> TryCacheAsync(float[] qVector)
    {
        var hit = await _cache.LookupAsync(qVector);
        if (hit is not null && hit.Distance <= _threshold.Value)
        {
            _threshold.RecordHit();
            var sources = hit.Sources.Length > 0 ? hit.Sources.Split('|') : Array.Empty<string>();
            return new AnswerResult(hit.Answer, sources, FromCache: true, hit.Distance);
        }
        _threshold.RecordMiss(hit?.Distance);
        return null;
    }

    private async Task<AnswerResult> PersistAsync(
        string question, float[] qVector, string answer, IReadOnlyList<RetrievedChunk> chunks)
    {
        var sources = chunks.Select(c => c.Source).Distinct().ToList();
        await _cache.SaveAsync(question, qVector, answer, string.Join('|', sources));
        return new AnswerResult(answer, sources, FromCache: false, double.MaxValue);
    }

    private static string Join(IReadOnlyList<string> sources) => string.Join(",", sources);

    private static string BuildPrompt(string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        string context = chunks.Count == 0
            ? "(ilgili bağlam bulunamadı)"
            : string.Join("\n", chunks.Select(c => $"- [{c.Source}] {c.Content}"));

        return $"""
            Aşağıdaki bağlama dayanarak soruyu yanıtla.
            Bağlamda cevap yoksa "Bu bilgiye sahip değilim" de. Uydurma.

            BAĞLAM:
            {context}

            SORU: {question}
            """;
    }
}
