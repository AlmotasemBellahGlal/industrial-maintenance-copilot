using System.Security.Cryptography;
using System.Text;
namespace IndustrialCopilot.Evaluation;

/// <summary>UTF-8 text fingerprint with canonical LF line endings across Git checkouts.</summary>
public static class DatasetFingerprint
{
    public static string Compute(string text) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")))).ToLowerInvariant();
    public static string FromFile(string path) => Compute(File.ReadAllText(path));
}
