using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace RagMini;

/// <summary>
/// Dosyadan düz metin çıkarır: .md/.txt doğrudan, .pdf PdfPig ile,
/// .docx OpenXML ile. Yeni format eklemek = yeni bir case + uzantı.
/// </summary>
public static class DocumentReader
{
    public static readonly string[] Extensions = { ".md", ".txt", ".pdf", ".docx" };

    public static bool Supports(string path)
        => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static async Task<string> ReadAsync(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf"  => ReadPdf(path),
            ".docx" => ReadDocx(path),
            _       => await File.ReadAllTextAsync(path),
        };

    private static string ReadPdf(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private static string ReadDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        return doc.MainDocumentPart?.Document?.Body?.InnerText ?? "";
    }
}
