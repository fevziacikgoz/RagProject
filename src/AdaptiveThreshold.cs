using System.Text;

namespace RagMini;

/// <summary>
/// Önbellek eşiğini, son sorulardaki "kaçan" mesafelerin analizine göre kendisi ayarlar.
/// Her ReviseEvery soruda bir: az farkla kaçanlar varsa eşiği açar, yoksa hafif sıkar;
/// her zaman Floor..Cap bandında kalır (yanlış eşleşme güvenliği).
/// </summary>
public sealed class AdaptiveThreshold
{
    public double Value { get; private set; } = RagOptions.InitialThreshold;

    private int _winAsked, _winHits, _winMisses;
    private readonly List<double> _winMissDist = new();

    public void RecordHit() => _winHits++;

    public void RecordMiss(double? nearestDistance)
    {
        _winMisses++;
        if (nearestDistance is double d && double.IsFinite(d)) _winMissDist.Add(d);
    }

    /// <summary>Her soru sonrası çağrılır. Pencere dolunca eşiği günceller ve rapor döner; yoksa null.</summary>
    public string? Tick()
    {
        if (++_winAsked < RagOptions.ReviseEvery) return null;

        double old = Value;
        if (_winMissDist.Count > 0)
        {
            var near = _winMissDist.Where(d => d <= Value + 0.06).ToList();
            Value = near.Count > 0
                ? Math.Min(RagOptions.ThresholdCap, Value + (near.Average() + 0.01 - Value) * 0.5)
                : Math.Max(RagOptions.ThresholdFloor, Value - 0.01);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"\n── 📊 Adaptif eşik analizi (son {RagOptions.ReviseEvery} soru) ──");
        sb.AppendLine($"   önbellekten yanıtlanan : {_winHits}/{RagOptions.ReviseEvery}");
        sb.AppendLine($"   LLM'e giden            : {_winMisses}");
        if (_winMissDist.Count > 0)
            sb.AppendLine($"   kaçanların mesafesi    : ort {_winMissDist.Average():F3}, en yakın {_winMissDist.Min():F3}");
        sb.AppendLine($"   eşik                   : {old:F3} → {Value:F3}  (sınırlar {RagOptions.ThresholdFloor:F2}..{RagOptions.ThresholdCap:F2})");
        sb.Append("───────────────────────────────────────────");

        _winAsked = 0; _winHits = 0; _winMisses = 0; _winMissDist.Clear();
        return sb.ToString();
    }
}
