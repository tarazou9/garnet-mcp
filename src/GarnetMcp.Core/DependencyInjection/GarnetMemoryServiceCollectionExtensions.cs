using GarnetMcp.Core.Connection;
using GarnetMcp.Core.Embeddings;
using GarnetMcp.Core.Memory;
using GarnetMcp.Core.Vectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GarnetMcp.Core.DependencyInjection;

/// <summary>
/// One-line DI registration for the Garnet memory stack. Register the Garnet vector layer with
/// <see cref="AddGarnetMemoryCore"/>, then add an embedding provider (<see cref="AddFakeEmbeddings"/>
/// or <see cref="AddAzureOpenAIEmbeddings"/>). The MCP server binds these from config.
/// </summary>
public static class GarnetMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the vector client, key naming, and memory store. Register a connection separately
    /// with <see cref="AddLocalGarnetConnection"/>.
    /// Set <paramref name="logRedisCommands"/> to log the pasteable redis VSIM for each recall.
    /// </summary>
    public static IServiceCollection AddGarnetMemoryCore(this IServiceCollection services,
        string keyPrefix = "mem", bool logRedisCommands = false)
    {
        services.AddSingleton<IGarnetVectorClient>(sp => new GarnetVectorClient(
            sp.GetRequiredService<IGarnetConnectionFactory>(),
            sp.GetService<ILogger<GarnetVectorClient>>(),
            logRedisCommands));
        services.AddSingleton(new VectorSetKeyNaming(keyPrefix));
        services.AddSingleton<GarnetMemoryStore>();
        return services;
    }

    /// <summary>Connects to a local/self-hosted OSS Garnet (no auth, no TLS) — dev/default.</summary>
    public static IServiceCollection AddLocalGarnetConnection(this IServiceCollection services, LocalGarnetOptions? garnet = null)
    {
        services.AddSingleton<IGarnetConnectionFactory>(_ => new LocalGarnetConnectionFactory(garnet));
        return services;
    }

    /// <summary>Uses the deterministic fake embedding provider (tests / offline demos).</summary>
    public static IServiceCollection AddFakeEmbeddings(this IServiceCollection services, int dimensions = 8)
    {
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider(dimensions));
        return services;
    }

    /// <summary>Uses Azure OpenAI embeddings (keyless via DefaultAzureCredential unless an API key is set).</summary>
    public static IServiceCollection AddAzureOpenAIEmbeddings(this IServiceCollection services, AzureOpenAIOptions options)
    {
        services.AddSingleton<IEmbeddingProvider>(new AzureOpenAIEmbeddingProvider(options));
        return services;
    }
}
