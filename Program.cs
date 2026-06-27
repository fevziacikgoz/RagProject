using System.Text;
using OpenAI;
using RagMini;

// =====================================================================
//  RAG + Semantic Cache + Artımlı Indexleme + Hybrid Retrieval
//  Bu dosya sadece "composition root" + REPL'dir; iş mantığı src/ altında.
// =====================================================================

// Windows PowerShell dahil her terminalde Türkçe karakterler düzgün görünsün.
Console.OutputEncoding = Encoding.UTF8;

EnvLoader.Load();

string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new Exception("OPENAI_API_KEY tanımlı değil. .env dosyasına OPENAI_API_KEY=sk-... ekle.");

// ── Servisleri kur (manuel dependency wiring) ────────────────────────
var openAi    = new OpenAIClient(apiKey);
var embedder  = new EmbeddingService(openAi.GetEmbeddingClient(RagOptions.EmbeddingModel));
var chat      = new ChatService(openAi.GetChatClient(RagOptions.ChatModel));

var database  = await Database.CreateAsync(RagOptions.ConnectionString);
var docStore  = new DocumentStore(database.DataSource);
var cache     = new CacheStore(database.DataSource);
var retriever = new HybridRetriever(docStore);
var threshold = new AdaptiveThreshold();
var pipeline  = new RagPipeline(embedder, chat, retriever, cache, threshold);

// ── Artımlı indexleme ────────────────────────────────────────────────
string knowledgeDir = Path.Combine(AppContext.BaseDirectory, RagOptions.KnowledgeDir);
await new Indexer(docStore, cache, embedder, knowledgeDir).RunAsync();

// ── Soru-cevap döngüsü ───────────────────────────────────────────────
Console.WriteLine("Soru sor (çıkış için boş bırak):");

while (true)
{
    Console.Write("\n? ");
    string? question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question)) break;

    AnswerResult result = await pipeline.AskAsync(question);

    Console.WriteLine($"\n>> Cevap: {result.Answer}");
    Console.WriteLine(result.FromCache
        ? $"   (kaynak: ÖNBELLEK — LLM'e gidilmedi, mesafe={result.Distance:F3}, eşik={threshold.Value:F3})"
        : $"   (kaynak: LLM + {result.Sources.Count} parça)");

    if (result.Sources.Count > 0)
        Console.WriteLine($"   📎 Kaynaklar: {string.Join(", ", result.Sources)}");

    if (pipeline.TickThreshold() is string report)
        Console.WriteLine(report);
}

Console.WriteLine("Bitti.");
