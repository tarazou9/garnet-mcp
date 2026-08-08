using System.Security.Cryptography;
using System.Text;

namespace GarnetMcp.Core.Embeddings;

/// <summary>
/// Deterministic, offline embedding provider for tests: the same text always yields the same
/// unit-length vector, seeded from a stable hash. No network, no API key. It is NOT semantically
/// meaningful — tests only rely on determinism and correct dimensionality.
/// </summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    public string ModelName => "fake-embedding";
    public int Dimensions { get; }

    public FakeEmbeddingProvider(int dimensions = 8)
    {
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));
        Dimensions = dimensions;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        var seed = BitConverter.ToInt32(hash, 0);
        var rng = new Random(seed);

        var v = new float[Dimensions];
        double sumSq = 0;
        for (var i = 0; i < Dimensions; i++)
        {
            v[i] = (float)(rng.NextDouble() * 2 - 1);
            sumSq += v[i] * (double)v[i];
        }

        // Normalize to unit length so Cosine behaves cleanly.
        var norm = Math.Sqrt(sumSq);
        if (norm > 0)
            for (var i = 0; i < Dimensions; i++)
                v[i] = (float)(v[i] / norm);

        return Task.FromResult(v);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var list = new List<float[]>(texts.Count);
        foreach (var t in texts)
            list.Add(await EmbedAsync(t, cancellationToken).ConfigureAwait(false));
        return list;
    }
}
