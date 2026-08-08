using GarnetMcp.Core.Connection;
using GarnetMcp.Core.Vectors;
using StackExchange.Redis;
using Xunit;

namespace GarnetMcp.Tests;

/// <summary>
/// Integration tests against a local Garnet started with --enable-vector-set-preview true on
/// 127.0.0.1:6379. If Garnet is not reachable, the tests are skipped (not failed) so the suite
/// stays green in environments without a local server.
/// </summary>
public sealed class GarnetVectorClientTests : IAsyncLifetime
{
    private readonly LocalGarnetConnectionFactory _factory = new();
    private GarnetVectorClient _client = null!;
    private bool _available;

    // Unique key per run so repeated runs don't hit the "params locked on first VADD" rule.
    private readonly string _key = $"gmcp:test:{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        try
        {
            var db = await _factory.GetDatabaseAsync();
            await db.PingAsync();
            _available = true;
            _client = new GarnetVectorClient(_factory);
        }
        catch
        {
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_available)
        {
            try
            {
                var db = await _factory.GetDatabaseAsync();
                await db.KeyDeleteAsync(_key);
            }
            catch { /* best effort cleanup */ }
        }
        await _factory.DisposeAsync();
    }

    private void SkipIfUnavailable()
        => Skip.IfNot(_available, "Local Garnet not reachable on 127.0.0.1:6379.");

    [SkippableFact]
    public async Task Add_Then_Search_Ranks_Nearest_First()
    {
        SkipIfUnavailable();
        var opts = new VectorAddOptions { Metric = VectorDistanceMetric.Cosine };

        Assert.True(await _client.AddAsync(_key, "doc:1", new float[] { 0.10f, 0.20f, 0.90f }, opts));
        Assert.True(await _client.AddAsync(_key, "doc:2", new float[] { 0.11f, 0.19f, 0.88f }, opts));
        Assert.True(await _client.AddAsync(_key, "doc:3", new float[] { 0.90f, 0.10f, 0.10f }, opts));

        var hits = await _client.SearchAsync(_key, new float[] { 0.10f, 0.20f, 0.90f },
            new VectorSearchOptions { Count = 3, WithScores = true });

        Assert.Equal(3, hits.Count);
        Assert.Equal("doc:1", hits[0].Element);          // exact match ranks first
        Assert.NotNull(hits[0].Score);
        Assert.Equal("doc:3", hits[^1].Element);          // orthogonal vector ranks last
    }

    [SkippableFact]
    public async Task Add_SameElementId_IsNotDeduped_InThisBuild()
    {
        SkipIfUnavailable();
        // Garnet 2.0.1 (Vector Sets preview) does NOT dedup by element id: re-adding the same id
        // appends a second entry (size grows to 2, and VSIM returns the id twice). Callers must use
        // unique ids, or VREM before re-adding, to "update". Asserting reality so we design around it.
        Assert.True(await _client.AddAsync(_key, "dup", new float[] { 0.1f, 0.2f, 0.3f }));
        Assert.True(await _client.AddAsync(_key, "dup", new float[] { 0.9f, 0.8f, 0.7f }));

        var info = await _client.GetInfoAsync(_key);
        Assert.NotNull(info);
        Assert.Equal(2, info!.Size);
    }

    [SkippableFact]
    public async Task Search_With_Filter_Respects_Attributes()
    {
        SkipIfUnavailable();
        var opts = new VectorAddOptions { AttributesJson = "{\"user\":\"alice\",\"year\":2021}" };
        await _client.AddAsync(_key, "a", new float[] { 0.10f, 0.20f, 0.90f }, opts);
        await _client.AddAsync(_key, "b", new float[] { 0.11f, 0.19f, 0.88f },
            new VectorAddOptions { AttributesJson = "{\"user\":\"alice\",\"year\":2010}" });
        await _client.AddAsync(_key, "c", new float[] { 0.12f, 0.18f, 0.87f },
            new VectorAddOptions { AttributesJson = "{\"user\":\"bob\",\"year\":2024}" });

        var recent = await _client.SearchAsync(_key, new float[] { 0.10f, 0.20f, 0.90f },
            new VectorSearchOptions { Count = 5, Filter = ".year >= 2020" });

        var elements = recent.Select(h => h.Element).ToHashSet();
        Assert.Contains("a", elements);
        Assert.Contains("c", elements);
        Assert.DoesNotContain("b", elements);
    }

    [SkippableFact]
    public async Task GetAttributes_Roundtrips_Json()
    {
        SkipIfUnavailable();
        await _client.AddAsync(_key, "x", new float[] { 0.1f, 0.2f, 0.3f },
            new VectorAddOptions { AttributesJson = "{\"user\":\"alice\"}" });

        var attrs = await _client.GetAttributesAsync(_key, "x");
        Assert.NotNull(attrs);
        Assert.Contains("alice", attrs);
    }

    [SkippableFact]
    public async Task Dim_And_Info_Reflect_The_Index()
    {
        SkipIfUnavailable();
        await _client.AddAsync(_key, "x", new float[] { 0.1f, 0.2f, 0.3f },
            new VectorAddOptions { Metric = VectorDistanceMetric.Cosine });

        Assert.Equal(3, await _client.GetDimensionsAsync(_key));

        var info = await _client.GetInfoAsync(_key);
        Assert.NotNull(info);
        Assert.Equal(3, info!.InputDimensions);
        Assert.True(info.Size >= 1);
    }

    [SkippableFact]
    public async Task Remove_Deletes_Element()
    {
        SkipIfUnavailable();
        await _client.AddAsync(_key, "gone", new float[] { 0.1f, 0.2f, 0.3f });
        Assert.True(await _client.RemoveAsync(_key, "gone"));
        Assert.False(await _client.RemoveAsync(_key, "gone"));
    }

    [SkippableFact]
    public async Task GetVector_Returns_Null_For_Missing_Key()
    {
        SkipIfUnavailable();
        var missing = await _client.GetVectorAsync($"gmcp:nope:{Guid.NewGuid():N}", "x");
        Assert.Null(missing);
    }

    [Fact]
    public void KeyNaming_Builds_Stable_Key_Per_Model_And_Metric()
    {
        var naming = new VectorSetKeyNaming("mem");
        Assert.Equal("mem:text-embedding-3-small:cosine",
            naming.For("text-embedding-3-small", VectorDistanceMetric.Cosine));
        // Whitespace/colons in the model are sanitized so the ':' separators stay unambiguous.
        Assert.Equal("mem:my-model:l2", naming.For("my model", VectorDistanceMetric.L2));
        Assert.Equal("mem:*", naming.Pattern);
    }

    [SkippableFact]
    public async Task ListIndexes_Finds_Created_Keys_By_Prefix()
    {
        SkipIfUnavailable();
        var naming = new VectorSetKeyNaming($"gmcptest{Guid.NewGuid():N}");
        var k1 = naming.For("model-a", VectorDistanceMetric.Cosine);
        var k2 = naming.For("model-b", VectorDistanceMetric.L2);
        try
        {
            await _client.AddAsync(k1, "e", new float[] { 0.1f, 0.2f, 0.3f });
            await _client.AddAsync(k2, "e", new float[] { 0.1f, 0.2f, 0.3f });

            var keys = await _client.ListIndexesAsync(naming.Pattern);
            Assert.Contains(k1, keys);
            Assert.Contains(k2, keys);
        }
        finally
        {
            var db = await _factory.GetDatabaseAsync();
            await db.KeyDeleteAsync(new StackExchange.Redis.RedisKey[] { k1, k2 });
        }
    }
}
