namespace RagMini;

/// <summary>
/// Hybrid retrieval + re-ranking + relevans filtresi:
///  1) DocumentStore'dan vektör + kelime adaylarını al
///  2) kombine skora göre yeniden sırala (re-rank)
///  3) çok uzak (alakasız) parçaları ele (relevans eşiği)
///  4) en iyi TopK parçayı döndür
/// </summary>
public sealed class HybridRetriever
{
    private readonly DocumentStore _store;
    public HybridRetriever(DocumentStore store) => _store = store;

    public async Task<List<RetrievedChunk>> RetrieveAsync(float[] qVector, string questionText)
    {
        var candidates = await _store.RetrieveCandidatesAsync(qVector, questionText, RagOptions.CandidateLimit);

        return candidates
            .Where(c => c.VectorDistance <= RagOptions.MaxDistance)                               // relevans
            .OrderByDescending(c => c.Score(RagOptions.VectorWeight, RagOptions.LexicalWeight))   // re-rank
            .Take(RagOptions.TopK)
            .ToList();
    }
}
