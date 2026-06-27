using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using RagMini;

// =====================================================================
//  RAG Web — ASP.NET Core Minimal API + SSE streaming + statik chat UI
//  Çekirdek (src/) aynı; burada sadece HTTP katmanı var.
// =====================================================================

Console.OutputEncoding = Encoding.UTF8;
var builder = WebApplication.CreateBuilder(args);

EnvLoader.Load();
string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new Exception("OPENAI_API_KEY tanımlı değil. .env dosyasına OPENAI_API_KEY=sk-... ekle.");

// ── Servisleri kur (sağlayıcı tek noktada) ───────────────────────────
var openAi    = new OpenAIClient(apiKey);
var embedder  = new EmbeddingService(openAi.GetEmbeddingClient(RagOptions.EmbeddingModel).AsIEmbeddingGenerator());
var chat      = new ChatService(openAi.GetChatClient(RagOptions.ChatModel).AsIChatClient());

var database  = await Database.CreateAsync(RagOptions.ConnectionString);
var docStore  = new DocumentStore(database.DataSource);
var cache     = new CacheStore(database.DataSource);
var retriever = new HybridRetriever(docStore);
var threshold = new AdaptiveThreshold();
var pipeline  = new RagPipeline(embedder, chat, retriever, cache, threshold);

var app = builder.Build();
app.UseDefaultFiles();   // / → wwwroot/index.html
app.UseStaticFiles();

// ── İndexlemeyi ARKA PLANDA başlat: sunucu hemen dinler, UI spinner gösterir ──
string knowledgeDir = Path.Combine(AppContext.BaseDirectory, RagOptions.KnowledgeDir);
var indexing = Task.Run(() => new Indexer(docStore, cache, embedder, knowledgeDir).RunAsync());

// ── Hazır mı? (UI spinner bunu yoklar) ───────────────────────────────
app.MapGet("/api/status", () => Results.Json(new
{
    ready  = indexing.IsCompletedSuccessfully,
    failed = indexing.IsFaulted,
    error  = indexing.IsFaulted ? indexing.Exception?.GetBaseException().Message : null
}));

// ── SSE streaming: cevabı token token akıtır ─────────────────────────
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
