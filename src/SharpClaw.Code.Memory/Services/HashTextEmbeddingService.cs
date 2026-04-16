using System.Globalization;
using System.Text.Json;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Generates deterministic local embeddings so semantic ranking works without a remote embedding model.
/// </summary>
internal static class HashTextEmbeddingService
{
    private const int Dimensions = 64;

    public static float[] Embed(string? text)
    {
        var vector = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(text))
        {
            return vector;
        }

        foreach (var token in Tokenize(text))
        {
            var hash = string.GetHashCode(token, StringComparison.Ordinal);
            var index = Math.Abs(hash % Dimensions);
            vector[index] += 1f;
        }

        Normalize(vector);
        return vector;
    }

    public static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return 0d;
        }

        double dot = 0;
        double leftMag = 0;
        double rightMag = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftMag += left[i] * left[i];
            rightMag += right[i] * right[i];
        }

        if (leftMag <= 0 || rightMag <= 0)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(leftMag) * Math.Sqrt(rightMag));
    }

    public static string Serialize(float[] vector)
        => JsonSerializer.Serialize(vector);

    public static float[] Deserialize(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new float[Dimensions]
            : JsonSerializer.Deserialize<float[]>(json) ?? new float[Dimensions];

    private static void Normalize(float[] vector)
    {
        double magnitude = 0;
        foreach (var value in vector)
        {
            magnitude += value * value;
        }

        if (magnitude <= 0)
        {
            return;
        }

        var scale = 1d / Math.Sqrt(magnitude);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] * scale);
        }
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var buffer = new List<char>(32);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                buffer.Add(char.ToLowerInvariant(ch));
                continue;
            }

            if (buffer.Count == 0)
            {
                continue;
            }

            yield return new string([.. buffer]);
            buffer.Clear();
        }

        if (buffer.Count > 0)
        {
            yield return new string([.. buffer]);
        }
    }
}
