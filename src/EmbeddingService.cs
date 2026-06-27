using Microsoft.Extensions.AI;

namespace RagMini;

/// <summary>Metni vektöre çevirir — MEAI IEmbeddingGenerator üzerinden (sağlayıcı-bağımsız).</summary>
public sealed class EmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> generator) => _generator = generator;

    public async Task<float[]> EmbedAsync(string text)
    {
        Embedding<float> embedding = await _generator.GenerateAsync(text);
        return embedding.Vector.ToArray();
    }
}
