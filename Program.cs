using Npgsql;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using Pgvector;
using System.Security.Cryptography;
using System.Text;

// =====================================================================
//  RAG + SEMANTIC CACHE + ARTIMLI INDEXLEME
//  .NET + pgvector + OpenAI
//  Akis:  WATCH(klasor) -> CHUNK -> EMBED -> STORE
//         -> (SEMANTIC CACHE?) -> RETRIEVE -> GENERATE
// =====================================================================

// ── 0. AYARLAR ───────────────────────────────────────────────────────
// .env dosyasi varsa oku. Boylece anahtari KODA yazmadan, .env icine
// OPENAI_API_KEY=sk-... koyabilirsin. .env, .gitignore'da -> repoya gitmez.
foreach (var envPath in new[] { ".env", Path.Combine(AppContext.BaseDirectory, ".env") })
{
    if (!File.Exists(envPath)) continue;
    foreach (var raw in File.ReadAllLines(envPath))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#")) continue;
        int eq = line.IndexOf('=');
        if (eq <= 0) continue;
        Environment.SetEnvironmentVariable(line[..eq].Trim(), line[(eq + 1)..].Trim().Trim('"'));
    }
    break;
}

// API anahtari ASLA koda gomulmez; .env'den veya ortam degiskeninden okunur.
string openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new Exception("OPENAI_API_KEY tanimli degil. .env dosyasina OPENAI_API_KEY=sk-... ekle.");

string connString  = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
string knowledgeDir = Path.Combine(AppContext.BaseDirectory, "knowledge");

// Onbellek esigi ARTIK ADAPTIF: her REVISE_EVERY soruda bir, sorularin mesafe
// analizine gore kendini gunceller (FLOOR..CAP araliginda).
// pgvector cosine distance: 0 = ayni anlam, buyudukce farklilasir.
// CAP kritiktir: cok yukselirse "senior" vs "junior" gibi yakin sorular yanlis eslesir.
double threshold       = 0.20;  // baslangic esigi (kendini ayarlar)
const int REVISE_EVERY = 3;     // kac soruda bir esik revize edilsin
const double FLOOR     = 0.10;  // esik bunun altina inmez
const double CAP       = 0.28;  // esik bunun ustune cikmaz (yanlis eslesme guvenligi)
int winAsked = 0, winHits = 0, winMisses = 0;   // pencere sayaclari
var winMissDist = new List<double>();           // penceredeki "kacan" sorularin mesafeleri

var openAi   = new OpenAIClient(openAiKey);
var embedder = openAi.GetEmbeddingClient("text-embedding-3-small"); // 1536 boyutlu vektor uretir
var chat     = openAi.GetChatClient("gpt-4o-mini");                 // ucuz ve yeterli

// pgvector'un "vector" tipini Npgsql'e taniyalim
var dsBuilder = new NpgsqlDataSourceBuilder(connString);
dsBuilder.UseVector();
await using var db = dsBuilder.Build();

// =====================================================================
//  SEMA  (her iki tablo da KALICI)
//   docs      : her satir bir chunk; source + content_hash ile artimli yonetilir
//   qa_cache  : soru-cevap onbellegi
// =====================================================================
await using (var schema = db.CreateCommand("""
    CREATE EXTENSION IF NOT EXISTS vector;

    CREATE TABLE IF NOT EXISTS docs (
        id           SERIAL PRIMARY KEY,
        source       TEXT,             -- hangi dosyadan geldi
        content      TEXT,
        embedding    VECTOR(1536),     -- text-embedding-3-small = 1536 boyut
        content_hash TEXT              -- kaynak dosyanin icerik parmak izi (SHA-256)
    );

    CREATE TABLE IF NOT EXISTS qa_cache (
        id         SERIAL PRIMARY KEY,
        question   TEXT,
        embedding  VECTOR(1536),
        answer     TEXT,
        created_at TIMESTAMPTZ DEFAULT now()
    );
    """))
{
    await schema.ExecuteNonQueryAsync();
}

// =====================================================================
//  ARTIMLI INDEXLEME (icerik-hash tabanli)
//  knowledge/ klasorundeki her .md dosyasi icin:
//   - hash DB'de yok          -> YENI  : chunk'la, embed et, ekle
//   - hash farkli (icerik degisti) -> GUNCEL: eski parcalari sil, yeniden ekle
//   - hash ayni               -> ATLA  : embed cagrisi yapma (para harcamadan gec)
//  Klasorde olmayip DB'de kalan dosyalar -> SIL.
// =====================================================================
Console.WriteLine(">> Dokumanlar taraniyor...");

int yeni = 0, guncel = 0, atlanan = 0, silinen = 0;
var diskteki = new HashSet<string>();

foreach (var file in Directory.GetFiles(knowledgeDir, "*.md"))
{
    string source = Path.GetFileName(file);
    diskteki.Add(source);

    string text = await File.ReadAllTextAsync(file);
    string hash = Sha256(text);

    // DB'de bu dosyanin kayitli hash'i
    string? dbHash = null;
    await using (var q = db.CreateCommand("SELECT content_hash FROM docs WHERE source = $1 LIMIT 1"))
    {
        q.Parameters.AddWithValue(source);
        if (await q.ExecuteScalarAsync() is string h) dbHash = h;
    }

    // Icerik degismemis -> atla
    if (dbHash == hash)
    {
        atlanan++;
        continue;
    }

    bool oncedenVardi = dbHash is not null;

    // Eski parcalari temizle (DELETE), sonra yeniden ekle (INSERT)
    await using (var del = db.CreateCommand("DELETE FROM docs WHERE source = $1"))
    {
        del.Parameters.AddWithValue(source);
        await del.ExecuteNonQueryAsync();
    }

    // CHUNKING: bos olmayan ve baslik (#) olmayan her satir bir parca
    var chunks = text.Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith("#"))
        .ToArray();

    foreach (var chunk in chunks)
    {
        float[] v = await EmbedAsync(chunk);
        await using var ins = db.CreateCommand(
            "INSERT INTO docs (source, content, embedding, content_hash) VALUES ($1, $2, $3, $4)");
        ins.Parameters.AddWithValue(source);
        ins.Parameters.AddWithValue(chunk);
        ins.Parameters.AddWithValue(new Vector(v));
        ins.Parameters.AddWithValue(hash);
        await ins.ExecuteNonQueryAsync();
    }

    if (oncedenVardi) { guncel++; Console.WriteLine($"   ~ güncellendi: {source} ({chunks.Length} parça)"); }
    else             { yeni++;   Console.WriteLine($"   + yeni: {source} ({chunks.Length} parça)"); }
}

// Klasorden silinmis dosyalarin DB kayitlarini temizle
var dbSources = new List<string>();
await using (var allSrc = db.CreateCommand("SELECT DISTINCT source FROM docs"))
await using (var r = await allSrc.ExecuteReaderAsync())
    while (await r.ReadAsync()) dbSources.Add(r.GetString(0));

foreach (var src in dbSources.Where(s => !diskteki.Contains(s)))
{
    await using var del = db.CreateCommand("DELETE FROM docs WHERE source = $1");
    del.Parameters.AddWithValue(src);
    await del.ExecuteNonQueryAsync();
    silinen++;
    Console.WriteLine($"   - silindi: {src}");
}

// Dokumanlar degistiyse onbellek bayatlamis olabilir -> temizle (kullanan yanilmasin)
if (yeni + guncel + silinen > 0)
{
    await using var clear = db.CreateCommand("DELETE FROM qa_cache");
    int n = await clear.ExecuteNonQueryAsync();
    if (n > 0) Console.WriteLine($">> Dökümanlar değişti; önbellek temizlendi ({n} kayıt).");
}

Console.WriteLine($">> İndexleme bitti. yeni={yeni}, güncel={guncel}, atlanan={atlanan}, silinen={silinen}\n");

// =====================================================================
//  QUERY  (kullanici her soru sordugunda)
//   1) Soruyu embed et
//   2) ONBELLEK: cok benzer bir soru daha once soruldu mu? -> LLM'siz cevap
//   3) Yoksa: RETRIEVE (en yakin 3 parca) -> GENERATE (LLM) -> cache'e yaz
// =====================================================================
Console.WriteLine("Soru sor (cikis icin bos birak):");

while (true)
{
    Console.Write("\n? ");
    string? question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question)) break;

    // (1) Soruyu da ayni embedding modeline cevir (ucuz adim).
    float[] qVector = await EmbedAsync(question);

    // (2) SEMANTIC CACHE: en yakin onceki soruyu getir; yeterince yakinsa LLM'e gitme.
    string? cachedAnswer = null;
    double cachedDist = double.MaxValue;
    await using (var lookup = db.CreateCommand(
        "SELECT answer, embedding <=> $1 AS dist FROM qa_cache ORDER BY embedding <=> $1 LIMIT 1"))
    {
        lookup.Parameters.AddWithValue(new Vector(qVector));
        await using var r = await lookup.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            cachedAnswer = r.GetString(0);
            cachedDist   = r.GetDouble(1);
        }
    }

    bool isHit = cachedAnswer is not null && cachedDist <= threshold;

    if (isHit)
    {
        // (2) ONBELLEK ISABETI: LLM'e hic gitmeden eski cevabi don.
        winHits++;
        Console.WriteLine($"\n>> Cevap: {cachedAnswer}");
        Console.WriteLine($"   (kaynak: ÖNBELLEK — LLM'e gidilmedi, mesafe={cachedDist:F3}, eşik={threshold:F3})");
    }
    else
    {
        // (3) Onbellek iskalandi -> klasik RAG.
        winMisses++;
        if (cachedAnswer is not null) winMissDist.Add(cachedDist); // "az farkla mi kacti?" analizi icin

        // (3a) RETRIEVAL: cosine distance (<=>) ile en yakin 3 parca.
        var docHits = new List<string>();
        await using (var search = db.CreateCommand(
            "SELECT content FROM docs ORDER BY embedding <=> $1 LIMIT 3"))
        {
            search.Parameters.AddWithValue(new Vector(qVector));
            await using var reader = await search.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                docHits.Add(reader.GetString(0));
        }

        // (3b) AUGMENTED PROMPT: cekilen baglami soruyla birlestir.
        string context = string.Join("\n- ", docHits);
        string prompt = $"""
            Aşağıdaki bağlama dayanarak soruyu yanıtla.
            Bağlamda cevap yoksa "Bu bilgiye sahip değilim" de. Uydurma.

            BAĞLAM:
            - {context}

            SORU: {question}
            """;

        // (3c) GENERATION: LLM baglamla cevap uretir.
        ChatCompletion completion = await chat.CompleteChatAsync(
            new SystemChatMessage("Sen bir İK asistanısın. Sadece verilen bağlamı kullan."),
            new UserChatMessage(prompt));
        string answerText = completion.Content[0].Text;

        // (3d) Cevabi ONBELLEGE yaz: bir sonraki benzer soru LLM'e gitmesin.
        await using (var save = db.CreateCommand(
            "INSERT INTO qa_cache (question, embedding, answer) VALUES ($1, $2, $3)"))
        {
            save.Parameters.AddWithValue(question);
            save.Parameters.AddWithValue(new Vector(qVector));
            save.Parameters.AddWithValue(answerText);
            await save.ExecuteNonQueryAsync();
        }

        string yakinlik = cachedAnswer is null
            ? "; önbellek boştu"
            : $"; en yakın önceki soru {cachedDist:F3} uzaktaydı (eşik {threshold:F3})";
        Console.WriteLine($"\n>> Cevap: {answerText}");
        Console.WriteLine($"   (kaynak: LLM + {docHits.Count} parça — önbelleğe eklendi{yakinlik})");
    }

    // ── ADAPTIF ESIK: her REVISE_EVERY soruda bir pencereyi analiz edip esigi guncelle ──
    winAsked++;
    if (winAsked >= REVISE_EVERY)
    {
        double oldThreshold = threshold;
        if (winMissDist.Count > 0)
        {
            // Esige YAKIN kacanlar (esik + 0.06'ya kadar) = "az farkla kacti" -> esigi onlara dogru ac.
            var nearMisses = winMissDist.Where(d => d <= threshold + 0.06).ToList();
            if (nearMisses.Count > 0)
                threshold = Math.Min(CAP, threshold + (nearMisses.Average() + 0.01 - threshold) * 0.5);
            else
                threshold = Math.Max(FLOOR, threshold - 0.01); // hep uzaktan kaciyor -> hafifce sikilas
        }

        Console.WriteLine($"\n── 📊 Adaptif eşik analizi (son {REVISE_EVERY} soru) ──");
        Console.WriteLine($"   önbellekten yanıtlanan : {winHits}/{REVISE_EVERY}");
        Console.WriteLine($"   LLM'e giden            : {winMisses}");
        if (winMissDist.Count > 0)
            Console.WriteLine($"   kaçanların mesafesi    : ort {winMissDist.Average():F3}, en yakın {winMissDist.Min():F3}");
        Console.WriteLine($"   eşik                   : {oldThreshold:F3} → {threshold:F3}  (sınırlar {FLOOR:F2}..{CAP:F2})");
        Console.WriteLine("───────────────────────────────────────────");

        winAsked = 0; winHits = 0; winMisses = 0; winMissDist.Clear();
    }
}

Console.WriteLine("Bitti.");

// ── YARDIMCILAR ──────────────────────────────────────────────────────
async Task<float[]> EmbedAsync(string text)
{
    OpenAIEmbedding e = await embedder.GenerateEmbeddingAsync(text);
    return e.ToFloats().ToArray();
}

static string Sha256(string text)
    => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
