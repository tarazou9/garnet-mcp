using StackExchange.Redis;

namespace GarnetMcp.Core.Connection;

/// <summary>Options for connecting to a local (OSS, plaintext, no-auth) Garnet server.</summary>
public sealed class LocalGarnetOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6379;

    public ConfigurationOptions ToConfiguration()
    {
        var config = new ConfigurationOptions
        {
            EndPoints = { { Host, Port } },
            AbortOnConnectFail = false,
            Ssl = false,
            // Pin RESP2 so Vector Set replies (e.g. VSIM WITHSCORES WITHATTRIBS) are flat arrays,
            // matching our parser and redis-cli. RESP3 would return maps/nested arrays instead.
            Protocol = RedisProtocol.Resp2,
        };
        return config;
    }
}

/// <summary>
/// Connects to a local or self-hosted OSS Garnet (no auth, no TLS). This is the connection
/// used for both development and self-hosted OSS Garnet clusters, behind the
/// <see cref="IGarnetConnectionFactory"/> interface.
/// </summary>
public sealed class LocalGarnetConnectionFactory : IGarnetConnectionFactory
{
    private readonly LocalGarnetOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnectionMultiplexer? _connection;

    public LocalGarnetConnectionFactory(LocalGarnetOptions? options = null)
        => _options = options ?? new LocalGarnetOptions();

    public async ValueTask<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsConnected: true })
            return _connection;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsConnected: true })
                return _connection;

            _connection?.Dispose();
            _connection = await ConnectionMultiplexer.ConnectAsync(_options.ToConfiguration()).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        return connection.GetDatabase();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
