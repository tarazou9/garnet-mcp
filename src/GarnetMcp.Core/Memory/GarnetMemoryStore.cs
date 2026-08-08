using System.Text.Json;
using System.Text.Json.Serialization;
using GarnetMcp.Core.Embeddings;
using GarnetMcp.Core.Vectors;

namespace GarnetMcp.Core.Memory;

/// <summary>A stored memory returned from recall: the id, similarity score, original text, and metadata.</summary>
public sealed record MemoryHit(string Id, double? Score, string? Text, string? User, long? Timestamp);

/// <summary>
/// The bridge between embeddings and the Garnet vector client. Turns text into vectors and stores
/// them as memories (VADD), and recalls similar memories by meaning (VSIM), scoped by user.
/// Encodes the design rules: Cosine metric, one key per model+metric, unique element ids
/// (Garnet does not dedup — to update, forget then store).
/// </summary>
public sealed class GarnetMemoryStore
{
    private readonly IGarnetVectorClient _vectors;
    private readonly IEmbeddingProvider _embeddings;
    private readonly VectorSetKeyNaming _naming;
    private readonly VectorDistanceMetric _metric;
    private bool _indexChecked;

    public GarnetMemoryStore(IGarnetVectorClient vectors, IEmbeddingProvider embeddings,
        VectorSetKeyNaming? naming = null, VectorDistanceMetric metric = VectorDistanceMetric.Cosine)
    {
        _vectors = vectors;
        _embeddings = embeddings;
        _naming = naming ?? new VectorSetKeyNaming("mem");
        _metric = metric;
    }

    /// <summary>The vector-set key for the configured model + metric, e.g. mem:text-embedding-3-small:cosine.</summary>
    public string Key => _naming.For(_embeddings.ModelName, _metric);

    /// <summary>Embeds <paramref name="text"/> and stores it as a memory. Returns the new memory id.</summary>
    public async Task<string> StoreMemoryAsync(string text, string? user = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text must not be empty.", nameof(text));

        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var vector = await _embeddings.EmbedAsync(text, cancellationToken).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N"); // unique: Garnet does not dedup by element id
        var attributes = JsonSerializer.Serialize(new MemoryAttributes
        {
            Text = text,
            User = user,
            Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });

        await _vectors.AddAsync(Key, id, vector,
            new VectorAddOptions { Metric = _metric, AttributesJson = attributes },
            cancellationToken).ConfigureAwait(false);
        return id;
    }

    /// <summary>Embeds the query and returns the most similar memories, optionally scoped to a user.</summary>
    public async Task<IReadOnlyList<MemoryHit>> RecallMemoryAsync(string query, string? user = null,
        int topK = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query must not be empty.", nameof(query));

        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var vector = await _embeddings.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        var options = new VectorSearchOptions
        {
            Count = topK <= 0 ? 5 : topK,
            WithScores = true,
            WithAttributes = true,
            Filter = user is null ? null : $".user == \"{EscapeFilterValue(user)}\"",
        };

        var results = await _vectors.SearchAsync(Key, vector, options, cancellationToken).ConfigureAwait(false);
        return results.Select(ToHit).ToList();
    }

    /// <summary>Deletes a memory by id. Returns true if it existed.</summary>
    public Task<bool> ForgetMemoryAsync(string id, CancellationToken cancellationToken = default)
        => _vectors.RemoveAsync(Key, id, cancellationToken);

    /// <summary>
    /// Guards against a model/index mismatch: if the vector set already exists but was built for a
    /// different dimension than the current provider, fail fast with a clear message.
    /// </summary>
    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (_indexChecked) return;
        var info = await _vectors.GetInfoAsync(Key, cancellationToken).ConfigureAwait(false);
        if (info is not null && info.InputDimensions != _embeddings.Dimensions)
        {
            throw new InvalidOperationException(
                $"Vector set '{Key}' was built for {info.InputDimensions} dimensions but the embedding " +
                $"provider '{_embeddings.ModelName}' produces {_embeddings.Dimensions}. Use a different key " +
                "prefix or a model matching the existing index.");
        }
        _indexChecked = true;
    }

    private static MemoryHit ToHit(VectorSearchResult r)
    {
        MemoryAttributes? attrs = null;
        if (!string.IsNullOrEmpty(r.AttributesJson))
        {
            try { attrs = JsonSerializer.Deserialize<MemoryAttributes>(r.AttributesJson); }
            catch (JsonException) { /* tolerate non-conforming attributes */ }
        }
        return new MemoryHit(r.Element, r.Score, attrs?.Text, attrs?.User, attrs?.Ts);
    }

    // Vector Set FILTER string values are double-quoted; escape embedded quotes/backslashes.
    private static string EscapeFilterValue(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class MemoryAttributes
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("user")] public string? User { get; set; }
        [JsonPropertyName("ts")] public long? Ts { get; set; }
    }
}
