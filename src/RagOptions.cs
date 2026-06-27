namespace RagMini;

/// <summary>Tüm ayarlanabilir sabitler tek yerde.</summary>
public static class RagOptions
{
    // Bağlantı & modeller
    public const string ConnectionString =
        "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
    public const string EmbeddingModel    = "text-embedding-3-small";
    public const string ChatModel         = "gpt-4o-mini";
    public const int    EmbeddingDimensions = 1536;
    public const string KnowledgeDir      = "knowledge";

    // Retrieval (hybrid + re-rank + relevans)
    public const int    CandidateLimit = 10;   // her aramadan kaç aday çekilsin
    public const int    TopK           = 3;    // LLM'e giden parça sayısı
    public const double MaxDistance    = 0.60; // relevans filtresi: bundan UZAK parça alınmaz (deneysel)
    public const double VectorWeight   = 0.70; // re-rank: semantik ağırlık
    public const double LexicalWeight  = 0.30; // re-rank: kelime (trigram) ağırlığı

    // Adaptif önbellek eşiği
    public const double InitialThreshold = 0.20;
    public const int    ReviseEvery      = 3;
    public const double ThresholdFloor   = 0.10;
    public const double ThresholdCap     = 0.28; // güvenlik tavanı (senior/junior karışmasın)

    // Akıllı chunking (uzun metinler için)
    public const int ChunkMaxWords     = 60;
    public const int ChunkOverlapWords = 15;
}
