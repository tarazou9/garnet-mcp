using System.Text.RegularExpressions;

namespace GarnetMcp.Core.Vectors;

/// <summary>
/// Builds and matches vector-set keys with a stable convention: <c>{prefix}:{model}:{metric}</c>.
/// Rationale: the first <c>VADD</c> to a key locks in the metric, quantization, M, and EF for that
/// key, so we keep one key per embedding-model + metric configuration. Example:
/// <c>mem:text-embedding-3-small:cosine</c>.
/// </summary>
public sealed partial class VectorSetKeyNaming
{
    public string Prefix { get; }

    public VectorSetKeyNaming(string prefix = "mem")
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix must not be empty.", nameof(prefix));
        Prefix = Sanitize(prefix);
    }

    /// <summary>The key for a given embedding model + distance metric.</summary>
    public string For(string model, VectorDistanceMetric metric)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must not be empty.", nameof(model));
        return $"{Prefix}:{Sanitize(model)}:{metric.ToString().ToLowerInvariant()}";
    }

    /// <summary>A SCAN glob that matches all keys created under this prefix.</summary>
    public string Pattern => $"{Prefix}:*";

    private static string Sanitize(string value)
        => WhitespaceOrColon().Replace(value.Trim(), "-");

    [GeneratedRegex(@"[\s:]+")]
    private static partial Regex WhitespaceOrColon();
}
