using StackExchange.Redis;

namespace GarnetMcp.Core.Connection;

/// <summary>
/// Creates connections to a local/self-hosted OSS Garnet server. The vector command layer built
/// on top is connection-agnostic, so the rest of the app is unaffected by connection details.
/// </summary>
public interface IGarnetConnectionFactory : IAsyncDisposable
{
    /// <summary>Returns a shared, connected multiplexer, creating it on first use.</summary>
    ValueTask<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Convenience: the default database on the shared connection.</summary>
    ValueTask<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default);
}
