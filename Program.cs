using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using RagMini;

// =====================================================================
//  RAG — Web (ASP.NET Core) veya Eval modu
//  Web:   dotnet run            → http://localhost:5000
//  Eval:  dotnet run -- --eval  → soru setini çalıştır, skor + metrik bas
//  Metrikler her istekte query_log tablosuna kalıcı yazılır.
// =====================================================================

Console.OutputEncoding = Encoding.UTF8;

EnvLoader.Load();
string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new Exception("OPENAI_API_KEY tanımlı değil. .env dosyasına OPENAI_API_KEY=sk-... ekle.");

// ── Servisleri kur (hem eval hem web için ortak) ─────────────────────
var openAi    = new OpenAIClient(apiKey);
var embedder  = new EmbeddingService(openAi.GetEmbeddingClient(RagOptions.EmbeddingModel).AsIEmbeddingGenerator());
var chat      = new ChatService(openAi.GetChatClient(RagOptions.ChatModel).AsIChatClient());
var database  = await Database.CreateAsync(RagOptions.ConnectionString);
var docStore  = new DocumentStore(database.DataSource);
var cache     = new CacheStore(database.DataSource);
var retriever = new HybridRetriever(docStore);
var threshold = new AdaptiveThreshold();
var metrics   = new MetricsStore(database.DataSource);
var pipeline  = new RagPipeline(embedder, chat, retriever, cache, threshold, metrics);

string knowledgeDir = Path.Combine(AppContext.BaseDirectory, RagOptions.KnowledgeDir);

// ── EVAL MODU ────────────────────────────────────────────────────────
if (args.Contains("--eval"))
{
    await new Indexer(docStore, cache, embedder, knowledgeDir).RunAsync();
    string evalPath = Path.Combine(AppContext.BaseDirectory, "eval", "eval-set.json");
    await new EvalHarness(pipeline, metrics, evalPath).RunAsync();
    return;
}

// ── WEB MODU ─────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseDefaultFiles();   // / → wwwroot/index.html
app.UseStaticFiles();

// İndexlemeyi arka planda başlat: sunucu hemen dinler, UI spinner gösterir.
var indexing = Task.Run(() => new Indexer(docStore, cache, embedder, knowledgeDir).RunAsync());

app.MapGet("/api/status", () => Results.Json(new
{
    ready  = indexing.IsCompletedSuccessfully,
    failed = indexing.IsFaulted,
    error  = indexing.IsFaulted ? indexing.Exception?.GetBaseException().Message : null
}));

app.MapGet("/api/metrics", async () => Results.Json(await metrics.SnapshotAsync()));

app.MapGet("/api/ask/stream", async (HttpContext ctx, string? q) =>
{
    if (!indexing.IsCompletedSuccessfully) { ctx.Response.StatusCode = 503; return; }
    if (string.IsNullOrWhiteSpace(q))      { ctx.Response.StatusCode = 400; return; }

    ctx.Response.Headers.ContentType  = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    Task Send(object payload) =>
        ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, json)}\n\n")
           .ContinueWith(_ => ctx.Response.Body.FlushAsync()).Unwrap();

    var result = await pipeline.AskStreamingAsync(q, token => Send(new { type = "token", text = token }));

    if (pipeline.TickThreshold() is string report)
        await Send(new { type = "info", text = report });

    await Send(new { type = "done", fromCache = result.FromCache, sources = result.Sources, distance = result.Distance });
});

Console.WriteLine(">> Web arayüzü hazır. Tarayıcıda aç: http://localhost:5000");
app.Run();
