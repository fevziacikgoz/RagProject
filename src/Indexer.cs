namespace RagMini;

/// <summary>
/// Artımlı indexleme: knowledge/ klasörünü tarar; içerik hash'i değişen dosyaları
/// yeniden vektörler, değişmeyeni atlar, silineni temizler. Veri değişince cache'i boşaltır.
/// </summary>
public sealed class Indexer
{
    private readonly DocumentStore _store;
    private readonly CacheStore _cache;
    private readonly EmbeddingService _embedder;
    private readonly string _knowledgeDir;

    public Indexer(DocumentStore store, CacheStore cache, EmbeddingService embedder, string knowledgeDir)
    {
        _store = store;
        _cache = cache;
        _embedder = embedder;
        _knowledgeDir = knowledgeDir;
    }

    public async Task RunAsync()
    {
        Console.WriteLine(">> Dökümanlar taranıyor...");
        int yeni = 0, guncel = 0, atlanan = 0, silinen = 0;
        var diskte = new HashSet<string>();

        foreach (var file in Directory.GetFiles(_knowledgeDir, "*.md"))
        {
            string source = Path.GetFileName(file);
            diskte.Add(source);

            string text = await File.ReadAllTextAsync(file);
            string hash = Hashing.Sha256(text);

            // İçerik hash'i aynıysa dosya değişmemiştir -> yeniden embed etme (boşa para yok).
            string? dbHash = await _store.GetHashAsync(source);
            if (dbHash == hash)
            {
                atlanan++;
                continue;
            }

            bool vardi = dbHash is not null;
            await _store.DeleteAsync(source);

            var chunks = Chunker.Chunk(text);
            foreach (var chunk in chunks)
                await _store.InsertAsync(source, chunk, await _embedder.EmbedAsync(chunk), hash);

            if (vardi) { guncel++; Console.WriteLine($"   ~ güncellendi: {source} ({chunks.Count} parça)"); }
            else       { yeni++;   Console.WriteLine($"   + yeni: {source} ({chunks.Count} parça)"); }
        }

        // Diskte olmayıp DB'de kalan dosyaları temizle.
        foreach (var src in (await _store.AllSourcesAsync()).Where(s => !diskte.Contains(s)))
        {
            await _store.DeleteAsync(src);
            silinen++;
            Console.WriteLine($"   - silindi: {src}");
        }

        // Herhangi bir döküman değiştiyse önbellek bayatlamış olabilir -> temizle.
        if (yeni + guncel + silinen > 0)
        {
            int n = await _cache.ClearAsync();
            if (n > 0) Console.WriteLine($">> Dökümanlar değişti; önbellek temizlendi ({n} kayıt).");
        }

        Console.WriteLine($">> İndexleme bitti. yeni={yeni}, güncel={guncel}, atlanan={atlanan}, silinen={silinen}\n");
    }
}
