namespace GarnetMcp.Core.Embeddings;

/// <summary>
/// Turns text into an embedding vector. Implementations can be a real model (Azure OpenAI) or a
/// deterministic fake for tests. The <see cref="ModelName"/> feeds the vector-set key and
/// <see cref="Dimensions"/> is used to validate the Garnet index matches the model.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Model id, e.g. "text-embedding-3-small". Used in the vector-set key name.</summary>
    string ModelName { get; }

    /// <summary>Vector length this model produces, e.g. 1536.</summary>
    int Dimensions { get; }

    /// <summary>Embed a single text.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embed many texts in one call (cheaper/faster than looping <see cref="EmbedAsync"/>).</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
