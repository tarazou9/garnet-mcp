namespace GarnetMcp.Core.Vectors;

/// <summary>
/// Options for adding a vector (<c>VADD</c>). Note: on the FIRST add to a key, the metric,
/// quantization, <see cref="NumLinks"/> and <see cref="BuildExplorationFactor"/> are LOCKED IN
/// for that key — later adds must match. Use one key per embedding-model + metric configuration.
/// </summary>
public sealed class VectorAddOptions
{
    public VectorQuantization Quantization { get; set; } = VectorQuantization.Q8;
    public VectorDistanceMetric Metric { get; set; } = VectorDistanceMetric.Cosine;

    /// <summary>HNSW links per node (Garnet default 16, range 4..4096). Null = server default.</summary>
    public int? NumLinks { get; set; }

    /// <summary>Build exploration factor (Garnet default 200). Null = server default.</summary>
    public int? BuildExplorationFactor { get; set; }

    /// <summary>Optional JSON attributes stored with the element (used later by search FILTER).</summary>
    public string? AttributesJson { get; set; }
}

/// <summary>Options for a similarity search (<c>VSIM</c>).</summary>
public sealed class VectorSearchOptions
{
    /// <summary>Number of results (top-K). Garnet default 10.</summary>
    public int Count { get; set; } = 10;

    /// <summary>Also return each hit's distance score.</summary>
    public bool WithScores { get; set; } = true;

    /// <summary>Also return each hit's JSON attributes.</summary>
    public bool WithAttributes { get; set; }

    /// <summary>Search exploration factor: higher = better recall, slower. Null = server default (100).</summary>
    public int? ExplorationFactor { get; set; }

    /// <summary>Search radius expansion factor. Null = server default (2.0).</summary>
    public double? Epsilon { get; set; }

    /// <summary>Attribute filter expression, e.g. <c>.year &gt;= 2020</c> or <c>.user == "alice"</c>.</summary>
    public string? Filter { get; set; }

    /// <summary>Adaptive filtering effort scale factor. Null = server default (16).</summary>
    public int? FilterExplorationFactor { get; set; }
}

/// <summary>A single similarity-search hit.</summary>
public sealed record VectorSearchResult(string Element, double? Score, string? AttributesJson);

/// <summary>Parsed <c>VINFO</c> metadata for a vector set key.</summary>
public sealed record VectorIndexInfo(
    string QuantizationType,
    string DistanceMetric,
    long InputDimensions,
    long ReducedDimensions,
    long BuildExplorationFactor,
    long NumLinks,
    long Size);
