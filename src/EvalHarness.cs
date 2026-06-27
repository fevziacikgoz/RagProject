using System.Text.Json;

namespace RagMini;

/// <summary>Bir değerlendirme sorusu: beklenen kelimeler ya da "reddetmeli" beklentisi.</summary>
public sealed record EvalItem(string Question, string[]? ExpectedKeywords, bool ShouldRefuse);

/// <summary>
/// Eval harness: sabit bir soru setini pipeline'a sorar, her cevabı beklentiyle
/// karşılaştırır (bağlam-içi: anahtar kelimeler var mı? / bağlam-dışı: reddetti mi?)
/// ve sonunda skor + (DB'den) metrik özeti basar.
/// </summary>
public sealed class EvalHarness
{
    private readonly RagPipeline _pipeline;
    private readonly MetricsStore _metrics;
    private readonly string _evalPath;

    public EvalHarness(RagPipeline pipeline, MetricsStore metrics, string evalPath)
    {
        _pipeline = pipeline;
        _metrics = metrics;
        _evalPath = evalPath;
    }

    public async Task RunAsync()
    {
        var items = JsonSerializer.Deserialize<EvalItem[]>(
            await File.ReadAllTextAsync(_evalPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        Console.WriteLine($">> Eval başlıyor: {items.Length} soru\n");
        int pass = 0;

        foreach (var item in items)
        {
            var result = await _pipeline.AskAsync(item.Question);
            string answer = result.Answer;
            bool refused = answer.Contains("bilgiye sahip değil", StringComparison.OrdinalIgnoreCase)
                        || answer.Contains("bilmiyorum", StringComparison.OrdinalIgnoreCase);

            bool ok;
            string reason;
            if (item.ShouldRefuse)
            {
                ok = refused;
                reason = ok ? "doğru reddetti" : "UYDURDU — reddetmeliydi";
            }
            else
            {
                var missing = (item.ExpectedKeywords ?? [])
                    .Where(k => !answer.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
                ok = !refused && missing.Count == 0;
                reason = ok ? "beklenen bilgi var"
                       : refused ? "boşuna reddetti"
                       : $"eksik kelime: {string.Join(", ", missing)}";
            }

            if (ok) pass++;
            Console.WriteLine($"  [{(ok ? "✓" : "✗")}] {item.Question}");
            if (!ok) Console.WriteLine($"         → {reason}");
        }

        double score = items.Length == 0 ? 0 : 100.0 * pass / items.Length;
        Console.WriteLine($"\n>> SKOR: {pass}/{items.Length}  ({score:F0}%)\n");
        Console.WriteLine(await _metrics.SummaryAsync());
    }
}
