namespace GarnetMcp.Core.Vectors;

/// <summary>
/// Thin, connection-agnostic wrapper over Garnet Vector Set commands (VADD/VSIM/VEMB/VDIM/
/// VINFO/VREM). Works against local or self-hosted OSS Garnet — the underlying connection is
/// abstracted. This is the layer the embedding/MCP-tool layers build on.
/// </summary>
public interface IGarnetVectorClient
{
    /// <summary>Adds a vector under <paramref name="element"/> in vector set <paramref name="key"/>.</summary>
    /// <returns><c>true</c> if the server acknowledged the add (Garnet returns 1).</returns>
    /// <remarks>
    /// NOTE: Garnet 2.0.1 (Vector Sets preview) does NOT dedup by element id: re-adding the same id
    /// appends a duplicate (search can then return the id more than once). Use unique element ids,
    /// or call <see cref="RemoveAsync"/> before re-adding, to update an existing memory.
    /// </remarks>
    Task<bool> AddAsync(string key, string element, ReadOnlyMemory<float> vector,
        VectorAddOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Similarity search using an explicit query vector.</summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string key, ReadOnlyMemory<float> query,
        VectorSearchOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Similarity search using an already-stored element's vector as the query ("more like this").</summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchByElementAsync(string key, string element,
        VectorSearchOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Removes one element. Returns <c>true</c> if it existed.</summary>
    Task<bool> RemoveAsync(string key, string element, CancellationToken cancellationToken = default);

    /// <summary>Returns the stored embedding for an element as floats, or null if not found.</summary>
    Task<float[]?> GetVectorAsync(string key, string element, CancellationToken cancellationToken = default);

    /// <summary>Returns the JSON attributes for an element, or null if none/not found.</summary>
    Task<string?> GetAttributesAsync(string key, string element, CancellationToken cancellationToken = default);

    /// <summary>Returns the dimensionality of the vector set, or null if the key does not exist.</summary>
    Task<long?> GetDimensionsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Returns parsed index metadata, or null if the key does not exist.</summary>
    Task<VectorIndexInfo?> GetInfoAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists vector-set keys matching a SCAN glob (default all). Garnet hides the internal
    /// namespaced graph records from SCAN, so this returns only top-level vector-set keys.
    /// </summary>
    Task<IReadOnlyList<string>> ListIndexesAsync(string pattern = "*", CancellationToken cancellationToken = default);
}
