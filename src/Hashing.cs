using System.Security.Cryptography;
using System.Text;

namespace RagMini;

public static class Hashing
{
    /// <summary>Dosya içeriğinin parmak izi — artımlı indexlemede "değişti mi?" için.</summary>
    public static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
