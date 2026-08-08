using GarnetMcp.Core.Connection;
using GarnetMcp.Core.Embeddings;
using GarnetMcp.Core.Memory;
using GarnetMcp.Core.Vectors;
using Xunit;

namespace GarnetMcp.Tests;

/// <summary>
/// Embedding + memory-store tests. The fake-provider determinism test is pure (no Garnet). The memory-store tests
/// run against local Garnet on 127.0.0.1:6379 and are skipped (not failed) if it is unreachable.
/// </summary>
public sealed class EmbeddingsAndMemoryTests : IAsyncLifetime
{
    private readonly LocalGarnetConnectionFactory _factory = new();
    private GarnetVectorClient _vectors = null!;
    private bool _available;
    private readonly List<string> _keysToClean = new();

    public async Task InitializeAsync()
    {
        try
        {
            var db = await _factory.GetDatabaseAsync();
            await db.PingAsync();
            _available = true;
            _vectors = new GarnetVectorClient(_factory);
        }
        catch { _available = false; }
    }

    public async Task DisposeAsync()
    {
        if (_available)
        {
            try
            {
                var db = await _factory.GetDatabaseAsync();
                foreach (var k in _keysToClean)
                    await db.KeyDeleteAsync(k);
            }
            catch { /* best effort */ }
        }
        await _factory.DisposeAsync();
    }

    private void SkipIfUnavailable()
        => Skip.IfNot(_available, "Local Garnet not reachable on 127.0.0.1:6379.");

    // Fresh store on a unique key prefix so tests don't collide or accumulate.
    private GarnetMemoryStore NewStore(int dims = 8, string? prefix = null)
    {
        var naming = new VectorSetKeyNaming(prefix ?? $"gmcpmem{Guid.NewGuid():N}");
        var store = new GarnetMemoryStore(_vectors, new FakeEmbeddingProvider(dims), naming);
        _keysToClean.Add(store.Key);
        return store;
    }

    [Fact]
    public async Task Fake_Provider_Is_Deterministic_And_Unit_Length()
    {
        var provider = new FakeEmbeddingProvider(16);
        Assert.Equal(16, provider.Dimensions);

        var a = await provider.EmbedAsync("hello world");
        var b = await provider.EmbedAsync("hello world");
        var c = await provider.EmbedAsync("something else");

        Assert.Equal(a, b);                 // same text -> identical vector
        Assert.NotEqual(a, c);              // different text -> different vector
        Assert.Equal(16, a.Length);
        var norm = Math.Sqrt(a.Sum(x => (double)x * x));
        Assert.InRange(norm, 0.999, 1.001); // unit length
    }

    [SkippableFact]
    public async Task Store_Then_Recall_Returns_Exact_Match_First()
    {
        SkipIfUnavailable();
        var store = NewStore();
        const string text = "the mitochondria is the powerhouse of the cell";
        var id = await store.StoreMemoryAsync(text, user: "alice");
        await store.StoreMemoryAsync("an unrelated note about databases", user: "alice");

        Assert.False(string.IsNullOrEmpty(id));

        // Recall with the exact text -> its deterministic vector matches, so it ranks first.
        var hits = await store.RecallMemoryAsync(text, user: "alice", topK: 5);
        Assert.NotEmpty(hits);
        Assert.Equal(id, hits[0].Id);
        Assert.Equal(text, hits[0].Text);
        Assert.Equal("alice", hits[0].User);
    }

    [SkippableFact]
    public async Task Recall_Respects_User_Filter()
    {
        SkipIfUnavailable();
        var store = NewStore();
        await store.StoreMemoryAsync("alice's secret plan", user: "alice");
        await store.StoreMemoryAsync("bob's grocery list", user: "bob");

        var hits = await store.RecallMemoryAsync("plan", user: "alice", topK: 10);
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal("alice", h.User));
        Assert.DoesNotContain(hits, h => h.Text == "bob's grocery list");
    }

    [SkippableFact]
    public async Task Forget_Removes_Memory()
    {
        SkipIfUnavailable();
        var store = NewStore();
        var id = await store.StoreMemoryAsync("temporary memory", user: "alice");

        Assert.True(await store.ForgetMemoryAsync(id));
        Assert.False(await store.ForgetMemoryAsync(id));
    }

    [SkippableFact]
    public async Task Dimension_Mismatch_Throws()
    {
        SkipIfUnavailable();
        // Same key prefix + same fake model name => same key; different dims => mismatch.
        var prefix = $"gmcpdim{Guid.NewGuid():N}";
        var store8 = NewStore(dims: 8, prefix: prefix);
        await store8.StoreMemoryAsync("built at 8 dims", user: "alice");

        var store16 = new GarnetMemoryStore(_vectors, new FakeEmbeddingProvider(16),
            new VectorSetKeyNaming(prefix));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store16.StoreMemoryAsync("would be 16 dims", user: "alice"));
    }
}
