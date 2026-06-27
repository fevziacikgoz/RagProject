using OpenAI.Embeddings;

namespace RagMini;

/// <summary>Metni vektöre çeviren ince OpenAI sarmalayıcısı.</summary>
public sealed class EmbeddingService
{
    private readonly EmbeddingClient _client;
    public EmbeddingService(EmbeddingClient client) => _client = client;

    public async Task<float[]> EmbedAsync(string text)
    {
        OpenAIEmbedding e = await _client.GenerateEmbeddingAsync(text);
        return e.ToFloats().ToArray();
    }
}
