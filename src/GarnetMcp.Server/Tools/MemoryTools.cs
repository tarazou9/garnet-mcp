using System.ComponentModel;
using System.Text.Json;
using GarnetMcp.Core.Memory;
using GarnetMcp.Core.Vectors;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using StackExchange.Redis;

namespace GarnetMcp.Server.Tools;

/// <summary>
/// Server-pinned memory settings. The memory owner (user) is fixed by configuration rather than
/// chosen by the model per-call, so store and recall always agree across chats/sessions. Multi-tenant
/// (model-supplied user) can be reintroduced later; for a personal memory server one user is correct.
/// </summary>
public sealed class MemoryToolsOptions
{
    public string User { get; init; } = "default";
}

/// <summary>
/// MCP tools that expose Garnet as an agent memory tier. Registered automatically via
/// WithToolsFromAssembly(); services (GarnetMemoryStore, IGarnetVectorClient, VectorSetKeyNaming,
/// MemoryToolsOptions) are injected into the tool method parameters by the MCP SDK.
/// </summary>
[McpServerToolType]
public static class MemoryTools
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [McpServerTool(Name = "store_memory"),
     Description("Persist a fact or piece of text to long-term memory. Use this whenever the user asks you to remember, save, note, or keep something for later — it stores the text durably in Garnet so it can be recalled in future sessions.")]
    public static Task<string> StoreMemory(
        GarnetMemoryStore store,
        MemoryToolsOptions options,
        ILoggerFactory loggerFactory,
        [Description("The text to remember")] string text,
        CancellationToken cancellationToken = default)
        => SafeAsync(loggerFactory, "store_memory", $"text=\"{Preview(text)}\"", async () =>
        {
            if (string.IsNullOrWhiteSpace(text)) return "Error: 'text' is empty.";
            var id = await store.StoreMemoryAsync(text, options.User, cancellationToken).ConfigureAwait(false);
            return $"Stored memory {id}.";
        });

    [McpServerTool(Name = "recall_memory"),
     Description("Look up previously stored memories by meaning (semantic search). Use this to answer questions about what the user told you to remember earlier — prefer it over relying on the current conversation, since memories persist across sessions.")]
    public static Task<string> RecallMemory(
        GarnetMemoryStore store,
        MemoryToolsOptions options,
        ILoggerFactory loggerFactory,
        [Description("What to search for")] string query,
        [Description("How many results to return (optional, default 5)")] int topK = 5,
        CancellationToken cancellationToken = default)
        => SafeAsync(loggerFactory, "recall_memory", $"query=\"{Preview(query)}\" topK={topK}", async () =>
        {
            if (string.IsNullOrWhiteSpace(query)) return "Error: 'query' is empty.";
            var hits = await store.RecallMemoryAsync(query, options.User, topK <= 0 ? 5 : topK, cancellationToken).ConfigureAwait(false);
            var shaped = hits.Select(h => new { id = h.Id, score = h.Score, text = h.Text, user = h.User });
            return JsonSerializer.Serialize(shaped, Json);
        });

    [McpServerTool(Name = "forget_memory"),
     Description("Delete a stored memory by its id (returned from store_memory).")]
    public static Task<string> ForgetMemory(
        GarnetMemoryStore store,
        ILoggerFactory loggerFactory,
        [Description("The memory id to delete")] string id,
        CancellationToken cancellationToken)
        => SafeAsync(loggerFactory, "forget_memory", $"id={id}", async () =>
        {
            if (string.IsNullOrWhiteSpace(id)) return "Error: 'id' is empty.";
            var removed = await store.ForgetMemoryAsync(id, cancellationToken).ConfigureAwait(false);
            return removed ? $"Forgot {id}." : $"No memory with id {id} was found.";
        });

    [McpServerTool(Name = "list_indexes"),
     Description("List the vector-set memory indexes that currently exist.")]
    public static Task<string> ListIndexes(
        IGarnetVectorClient vectors,
        VectorSetKeyNaming naming,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
        => SafeAsync(loggerFactory, "list_indexes", naming.Pattern, async () =>
        {
            var keys = await vectors.ListIndexesAsync(naming.Pattern, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(keys, Json);
        });

    [McpServerTool(Name = "index_info"),
     Description("Show metadata (metric, dimensions, size) for a memory index key.")]
    public static Task<string> IndexInfo(
        IGarnetVectorClient vectors,
        ILoggerFactory loggerFactory,
        [Description("The index key, e.g. mem:text-embedding-3-small:cosine")] string key,
        CancellationToken cancellationToken)
        => SafeAsync(loggerFactory, "index_info", $"key={key}", async () =>
        {
            if (string.IsNullOrWhiteSpace(key)) return "Error: 'key' is empty.";
            var info = await vectors.GetInfoAsync(key, cancellationToken).ConfigureAwait(false);
            return info is null ? $"No index '{key}' exists." : JsonSerializer.Serialize(info, Json);
        });

    // Runs a tool with logging (so you can SEE each invocation in the server logs) and maps common
    // Garnet failures to plain messages for the agent instead of raw stack traces.
    private static async Task<string> SafeAsync(ILoggerFactory loggerFactory, string tool, string args, Func<Task<string>> action)
    {
        var logger = loggerFactory.CreateLogger("GarnetMcp.Tools");
        logger.LogDebug("MCP tool called: {Tool} ({Args})", tool, args);
        try
        {
            var result = await action().ConfigureAwait(false);
            logger.LogDebug("MCP tool {Tool} -> {Result}", tool, Preview(result));
            return result;
        }
        catch (RedisConnectionException)
        {
            logger.LogWarning("MCP tool {Tool} failed: cannot reach Garnet", tool);
            return "Error: cannot reach Garnet. Is it running? Start a Garnet server with Vector Sets " +
                   "enabled (--enable-vector-set-preview true), or check the Garnet:Host/Port configuration.";
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Vector Set", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("MCP tool {Tool} failed: Vector Sets preview not enabled", tool);
            return "Error: Vector Sets are not enabled on this Garnet server. Start Garnet with " +
                   "--enable-vector-set-preview.";
        }
        catch (Exception ex) when (ex.Message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("MCP tool {Tool} failed: NOAUTH", tool);
            return "Error: Garnet rejected the command with NOAUTH. This is an auth/ACL issue, or a " +
                   "nodeless enumeration command (e.g. list_indexes) on an auth-enabled connection — " +
                   "store/recall/forget are unaffected.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP tool {Tool} failed", tool);
            return $"Error: {ex.Message}";
        }
    }

    private static string Preview(string? s, int max = 80)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\n', ' ').Replace('\r', ' ');
        return s.Length <= max ? s : s[..max] + "…";
    }
}
