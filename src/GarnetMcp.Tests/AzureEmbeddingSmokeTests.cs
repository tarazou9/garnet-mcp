using GarnetMcp.Core.Connection;
using GarnetMcp.Core.Embeddings;
using GarnetMcp.Core.Memory;
using GarnetMcp.Core.Vectors;
using Xunit;

namespace GarnetMcp.Tests;

/// <summary>
/// Smoke test against the REAL Azure OpenAI embedding resource (keyless).
/// Runs only when GARNET_AOAI_ENDPOINT is set; otherwise skipped so CI stays green.
/// Uses DefaultAzureCredential (your `az login` identity, which holds Cognitive Services OpenAI User).
/// </summary>
public sealed class AzureEmbeddingSmokeTests : IAsyncLifetime
{
    private readonly LocalGarnetConnectionFactory _factory = new();
    private GarnetVectorClient _vectors = null!;
    private bool _garnetAvailable;
    private string? _keyToClean;

    public async Task InitializeAsync()
    {
        try
        {
            var db = await _factory.GetDatabaseAsync();
            await db.PingAsync();
            _garnetAvailable = true;
            _vectors = new GarnetVectorClient(_factory);
        }
        catch { _garnetAvailable = false; }
    }

    public async Task DisposeAsync()
    {
        if (_garnetAvailable && _keyToClean is not null)
        {
            try { await (await _factory.GetDatabaseAsync()).KeyDeleteAsync(_keyToClean); }
            catch { /* best effort */ }
        }
        await _factory.DisposeAsync();
    }

    [SkippableFact]
    public async Task Azure_Embeddings_Store_And_Semantic_Recall()
    {
        var endpoint = Environment.GetEnvironmentVariable("GARNET_AOAI_ENDPOINT");
        Skip.If(string.IsNullOrEmpty(endpoint),
            "Set GARNET_AOAI_ENDPOINT (and be `az login`ed) to run the Azure OpenAI smoke test.");
        Skip.IfNot(_garnetAvailable, "Local Garnet not reachable on 127.0.0.1:6379.");

        var deployment = Environment.GetEnvironmentVariable("GARNET_AOAI_DEPLOYMENT") ?? "text-embedding-3-small";
        var dims = int.TryParse(Environment.GetEnvironmentVariable("GARNET_AOAI_DIMENSIONS"), out var d) ? d : 1536;

        var provider = new AzureOpenAIEmbeddingProvider(new AzureOpenAIOptions
        {
            Endpoint = endpoint!,
            DeploymentName = deployment,
            Dimensions = dims,
        });

        // 1) Real embedding call returns a vector of the expected size.
        var vector = await provider.EmbedAsync("hello world");
        Assert.Equal(dims, vector.Length);

        // 2) End-to-end: store two distinct memories, then recall by MEANING (not exact text).
        var store = new GarnetMemoryStore(_vectors, provider, new VectorSetKeyNaming($"gmcpaoai{Guid.NewGuid():N}"));
        _keyToClean = store.Key;

        await store.StoreMemoryAsync("The mitochondria is the powerhouse of the cell", user: "alice");
        await store.StoreMemoryAsync("My favorite NoSQL database is Azure Cosmos DB", user: "alice");

        var hits = await store.RecallMemoryAsync("what part of a cell produces energy", user: "alice", topK: 2);

        Assert.NotEmpty(hits);
        // Real embeddings should rank the biology memory above the database one for this query.
        Assert.Equal("The mitochondria is the powerhouse of the cell", hits[0].Text);
        Assert.All(hits, h => Assert.Equal("alice", h.User));
    }
}
