namespace RagMini;

/// <summary>
/// Çekirdek akış: soru → (semantic cache) → hybrid retrieve + re-rank →
/// augmented prompt → LLM → cache'e yaz. Tüm parçaları orkestra eder.
/// </summary>
public sealed class RagPipeline
{
    private const string SystemPrompt = "Sen bir İK asistanısın. Sadece verilen bağlamı kullan.";

    private readonly EmbeddingService _embedder;
    private readonly ChatService _chat;
    private readonly HybridRetriever _retriever;
    private readonly CacheStore _cache;
    private readonly AdaptiveThreshold _threshold;

    public RagPipeline(EmbeddingService embedder, ChatService chat, HybridRetriever retriever,
                       CacheStore cache, AdaptiveThreshold threshold)
    {
        _embedder = embedder;
        _chat = chat;
        _retriever = retriever;
        _cache = cache;
        _threshold = threshold;
    }

    public async Task<AnswerResult> AskAsync(string question)
    {
        float[] qVector = await _embedder.EmbedAsync(question);

        // (1) SEMANTIC CACHE — yeterince yakınsa LLM'e hiç gitme
        var hit = await _cache.LookupAsync(qVector);
        if (hit is not null && hit.Distance <= _threshold.Value)
        {
            _threshold.RecordHit();
            var cachedSources = hit.Sources.Length > 0 ? hit.Sources.Split('|') : Array.Empty<string>();
            return new AnswerResult(hit.Answer, cachedSources, FromCache: true, hit.Distance);
        }
        _threshold.RecordMiss(hit?.Distance);

        // (2) HYBRID RETRIEVAL + RE-RANK + RELEVANS
        var chunks = await _retriever.RetrieveAsync(qVector, question);

        // (3) AUGMENTED PROMPT — her parça kaynağıyla etiketli (citations)
        string context = chunks.Count == 0
            ? "(ilgili bağlam bulunamadı)"
            : string.Join("\n", chunks.Select(c => $"- [{c.Source}] {c.Content}"));

        string prompt = $"""
            Aşağıdaki bağlama dayanarak soruyu yanıtla.
            Bağlamda cevap yoksa "Bu bilgiye sahip değilim" de. Uydurma.

            BAĞLAM:
            {context}

            SORU: {question}
            """;

        // (4) GENERATE
        string answer = await _chat.AnswerAsync(SystemPrompt, prompt);

        // (5) CACHE'E YAZ (kaynaklarıyla birlikte)
        var sources = chunks.Select(c => c.Source).Distinct().ToList();
        await _cache.SaveAsync(question, qVector, answer, string.Join('|', sources));

        return new AnswerResult(answer, sources, FromCache: false, hit?.Distance ?? double.MaxValue);
    }

    public string? TickThreshold() => _threshold.Tick();
}
