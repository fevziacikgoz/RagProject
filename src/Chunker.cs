namespace RagMini;

/// <summary>
/// Akıllı chunking: markdown başlıklarını atlar, boş olmayan satırları parça yapar;
/// çok uzun satırları ChunkMaxWords pencerelerine ChunkOverlapWords örtüşmeyle böler
/// (uzun PDF/paragraflarda bağlamın kopmaması için).
/// </summary>
public static class Chunker
{
    public static List<string> Chunk(string text)
    {
        var result = new List<string>();

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'));

        foreach (var line in lines)
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length <= RagOptions.ChunkMaxWords)
            {
                result.Add(line);
                continue;
            }

            int step = Math.Max(1, RagOptions.ChunkMaxWords - RagOptions.ChunkOverlapWords);
            for (int i = 0; i < words.Length; i += step)
            {
                result.Add(string.Join(' ', words.Skip(i).Take(RagOptions.ChunkMaxWords)));
                if (i + RagOptions.ChunkMaxWords >= words.Length) break;
            }
        }
        return result;
    }
}
