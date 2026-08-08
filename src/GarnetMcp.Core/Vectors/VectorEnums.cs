namespace GarnetMcp.Core.Vectors;

/// <summary>How closeness between vectors is measured. Cosine is best for text embeddings.</summary>
public enum VectorDistanceMetric
{
    /// <summary>Straight-line (Euclidean) distance. Garnet default.</summary>
    L2,
    /// <summary>Angle-based similarity; recommended for text embeddings.</summary>
    Cosine,
    /// <summary>Dot product; typically used with normalized vectors.</summary>
    InnerProduct,
}

/// <summary>How each vector is compressed on storage. Q8 (8-bit) is the memory-friendly default.</summary>
public enum VectorQuantization
{
    /// <summary>Full precision (largest, most accurate).</summary>
    None,
    /// <summary>8-bit scalar quantization (~4x smaller, tiny accuracy loss). Garnet default.</summary>
    Q8,
    /// <summary>1-bit (smallest, roughest).</summary>
    Bin,
}

internal static class VectorEnumExtensions
{
    public static string ToMetricToken(this VectorDistanceMetric metric) => metric switch
    {
        VectorDistanceMetric.L2 => "L2",
        VectorDistanceMetric.Cosine => "COSINE",
        VectorDistanceMetric.InnerProduct => "IP",
        _ => "L2",
    };

    public static string? ToQuantToken(this VectorQuantization quant) => quant switch
    {
        VectorQuantization.None => "NOQUANT",
        VectorQuantization.Q8 => "Q8",
        VectorQuantization.Bin => "BIN",
        _ => null,
    };
}
