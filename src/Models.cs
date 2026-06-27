namespace RagMini;

/// <summary>Retrieval'dan dönen bir aday parça (hem semantik hem kelime skoruyla).</summary>
public record RetrievedChunk(string Source, string Content, double VectorDistance, double LexicalScore)
{
    /// <summary>Re-rank skoru: yakın vektör (1 - mesafe) + kelime benzerliği, ağırlıklı.</summary>
    public double Score(double wVector, double wLexical)
        => wVector * (1.0 - VectorDistance) + wLexical * LexicalScore;
}

/// <summary>Önbellekte bulunan en yakın soru-cevap.</summary>
public record CacheHit(string Answer, string Sources, double Distance);

/// <summary>Pipeline'ın bir soruya nihai yanıtı.</summary>
public record AnswerResult(string Answer, IReadOnlyList<string> Sources, bool FromCache, double Distance);
