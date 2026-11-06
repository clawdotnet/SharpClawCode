using System.Security.Cryptography;
using System.Text;

namespace SharpClaw.Code.Tools.Utilities;

/// <summary>Indexes original text without normalizing line endings or adding a phantom final line.</summary>
internal sealed class FileTextSnapshot(string text)
{
    internal string Text { get; } = text;
    internal string Id { get; } = ComputeId(text);
    internal int[] Starts { get; } = FindStarts(text);

    internal static string ComputeId(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal int Offset(int line) => line == Starts.Length + 1 ? Text.Length : Starts[line - 1];

    private static int[] FindStarts(string text)
    {
        if (text.Length == 0) return [];
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (text[i] is not ('\r' or '\n')) continue;
            if (i + 1 < text.Length) starts.Add(i + 1);
        }
        return starts.ToArray();
    }
}
