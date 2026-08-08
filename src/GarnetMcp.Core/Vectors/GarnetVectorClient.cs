using System.Globalization;
using System.Runtime.InteropServices;
using GarnetMcp.Core.Connection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GarnetMcp.Core.Vectors;

/// <summary>
/// Default <see cref="IGarnetVectorClient"/> built on StackExchange.Redis raw <c>Execute</c>
/// (there are no typed wrappers for Garnet Vector Set commands). Encodes vectors as FP32 blobs
/// and parses the RESP2 responses.
/// </summary>
public sealed class GarnetVectorClient : IGarnetVectorClient
{
    private readonly IGarnetConnectionFactory _connectionFactory;
    private readonly ILogger? _logger;
    private readonly bool _logCommands;

    public GarnetVectorClient(IGarnetConnectionFactory connectionFactory,
        ILogger<GarnetVectorClient>? logger = null, bool logCommands = false)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _logCommands = logCommands;
    }

    public async Task<bool> AddAsync(string key, string element, ReadOnlyMemory<float> vector,
        VectorAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key must not be empty.", nameof(key));
        if (vector.Length == 0) throw new ArgumentException("Vector must not be empty.", nameof(vector));
        options ??= new VectorAddOptions();

        var args = new List<object> { key, "FP32", ToFp32Bytes(vector), element };

        if (options.Quantization.ToQuantToken() is { } quant)
            args.Add(quant);
        if (options.BuildExplorationFactor is { } ef)
        {
            args.Add("EF");
            args.Add(ef);
        }
        if (options.NumLinks is { } m)
        {
            args.Add("M");
            args.Add(m);
        }
        if (!string.IsNullOrEmpty(options.AttributesJson))
        {
            args.Add("SETATTR");
            args.Add(options.AttributesJson);
        }
        args.Add("XDISTANCE_METRIC");
        args.Add(options.Metric.ToMetricToken());

        var db = await _connectionFactory.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var result = await db.ExecuteAsync("VADD", args.ToArray()).ConfigureAwait(false);
        return (long)result == 1;
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string key, ReadOnlyMemory<float> query,
        VectorSearchOptions? options = null, CancellationToken cancellationToken = default)
        => SearchCoreAsync(key, new object[] { "FP32", ToFp32Bytes(query) }, options, cancellationToken);

    public Task<IReadOnlyList<VectorSearchResult>> SearchByElementAsync(string key, string element,
        VectorSearchOptions? options = null, CancellationToken cancellationToken = default)
        => SearchCoreAsync(key, new object[] { "ELE", element }, options, cancellationToken);

    private async Task<IReadOnlyList<VectorSearchResult>> SearchCoreAsync(string key, object[] queryArgs,
        VectorSearchOptions? options, CancellationToken cancellationToken)
    {
        options ??= new VectorSearchOptions();
        var args = new List<object> { key };
        args.AddRange(queryArgs);

        if (options.WithScores) args.Add("WITHSCORES");
        if (options.WithAttributes) args.Add("WITHATTRIBS");
        args.Add("COUNT");
        args.Add(options.Count);
        if (options.ExplorationFactor is { } ef)
        {
            args.Add("EF");
            args.Add(ef);
        }
        if (options.Epsilon is { } eps)
        {
            args.Add("EPSILON");
            args.Add(eps.ToString(CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrEmpty(options.Filter))
        {
            args.Add("FILTER");
            args.Add(options.Filter);
        }
        if (options.FilterExplorationFactor is { } fef)
        {
            args.Add("FILTER-EF");
            args.Add(fef);
        }

        if (_logCommands && _logger is not null)
        {
            // Opt-in (Garnet:LogRedisCommands): a compact VSIM summary at Debug. The query vector
            // itself is deliberately omitted — logging ~1000+ floats per recall is not useful.
            _logger.LogDebug(
                "recall VSIM on {Key}: count={Count}, withScores={WithScores}, withAttrs={WithAttributes}, filter={Filter}",
                key, options.Count, options.WithScores, options.WithAttributes,
                string.IsNullOrEmpty(options.Filter) ? "(none)" : options.Filter);
        }

        var db = await _connectionFactory.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var result = await db.ExecuteAsync("VSIM", args.ToArray()).ConfigureAwait(false);
        return ParseSearchResults(result, options.WithScores, options.WithAttributes);
    }

    public async Task<bool> RemoveAsync(string key, string element, CancellationToken cancellationToken = default)
    {
        var db = await _connectionFactory.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var result = await db.ExecuteAsync("VREM", key, element).ConfigureAwait(false);
        return (long)result == 1;
    }

    public async Task<float[]?> GetVectorAsync(string key, string element, CancellationToken cancellationToken = default)
    {
        var db = await _connectionFactory.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await db.ExecuteAsync("VEMB", key, element).ConfigureAwait(false);
            if (result.IsNull || result.Resp2Type != ResultType.Array) return null;
            var items = (RedisResult[])result!;
            if (items.Length == 0) return null;
            var floats = new float[items.Length];
            for (var i = 0; i < items.Length; i++)
                floats[i] = float.Parse((string)items[i]!, CultureInfo.InvariantCulture);
            return floats;
        }
        catch (RedisServerException)
        {
            return null;
        }
    }

    public async Task<string?> GetAttributesAsync(string key, string element, CancellationToken cancellationToken = default)
    {
        var db = await _connectionFactory.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await db.ExecuteAsync("VGETATTR", key, element).ConfigureAwait(false);
            return result.IsNull ? null : (string?)result;
        }
        catch (RedisServerException)
        {
            return null;
        }
    }

    public async Task<long?> GetDimensionsAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = await _connectionFactory.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await db.ExecuteAsync("VDIM", key).ConfigureAwait(false);
            return result.IsNull ? null : (long)result;
        }
        catch (RedisServerException)
        {
            return null;
        }
    }

    public async Task<VectorIndexInfo?> GetInfoAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = await _connectionFactory.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await db.ExecuteAsync("VINFO", key).ConfigureAwait(false);
            if (result.IsNull || result.Resp2Type != ResultType.Array) return null;
            var items = (RedisResult[])result!;
            if (items.Length == 0) return null;

            var map = new Dictionary<string, RedisResult>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < items.Length; i += 2)
                map[(string)items[i]!] = items[i + 1];

            return new VectorIndexInfo(
                QuantizationType: GetString(map, "quant-type"),
                DistanceMetric: GetString(map, "distance-metric"),
                InputDimensions: GetLong(map, "input-vector-dimensions"),
                ReducedDimensions: GetLong(map, "reduced-dimensions"),
                BuildExplorationFactor: GetLong(map, "build-exploration-factor"),
                NumLinks: GetLong(map, "num-links"),
                Size: GetLong(map, "size"));
        }
        catch (RedisServerException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> ListIndexesAsync(string pattern = "*", CancellationToken cancellationToken = default)
    {
        // Enumerate via SCAN on the same command connection as VADD/VSIM (db.Execute), not
        // IServer.Keys: the latter uses a separate server-scoped connection that can fail with
        // NOAUTH against an auth-enabled endpoint. SCAN here works for both no-auth (default OSS)
        // and self-hosted auth-enabled Garnet/Redis.
        var db = await _connectionFactory.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var keys = new List<string>();
        var cursor = "0";
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await db.ExecuteAsync("SCAN", cursor, "MATCH", pattern, "COUNT", "100").ConfigureAwait(false);
            var top = (RedisResult[])result!;
            cursor = (string)top[0]!;
            foreach (var key in (RedisResult[])top[1]!)
                keys.Add((string)key!);
        }
        while (cursor != "0");
        return keys;
    }

    private static IReadOnlyList<VectorSearchResult> ParseSearchResults(RedisResult result, bool withScores, bool withAttributes)
    {
        if (result.IsNull || result.Resp2Type != ResultType.Array)
            return Array.Empty<VectorSearchResult>();

        var items = (RedisResult[])result!;
        var step = 1 + (withScores ? 1 : 0) + (withAttributes ? 1 : 0);
        var hits = new List<VectorSearchResult>(items.Length / step);

        for (var i = 0; i + step - 1 < items.Length; i += step)
        {
            var offset = i;
            var element = (string)items[offset++]!;
            double? score = null;
            if (withScores)
                score = double.Parse((string)items[offset++]!, CultureInfo.InvariantCulture);
            string? attrs = null;
            if (withAttributes)
                attrs = (string?)items[offset++];
            hits.Add(new VectorSearchResult(element, score, attrs));
        }
        return hits;
    }

    private static byte[] ToFp32Bytes(ReadOnlyMemory<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        MemoryMarshal.AsBytes(vector.Span).CopyTo(bytes);
        return bytes;
    }

    private static string GetString(Dictionary<string, RedisResult> map, string key)
        => map.TryGetValue(key, out var v) ? (string)v! ?? string.Empty : string.Empty;

    private static long GetLong(Dictionary<string, RedisResult> map, string key)
        => map.TryGetValue(key, out var v) && !v.IsNull ? (long)v : 0L;
}

