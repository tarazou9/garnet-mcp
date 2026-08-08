using GarnetMcp.Core.Connection;
using GarnetMcp.Core.DependencyInjection;
using GarnetMcp.Core.Embeddings;
using GarnetMcp.Server.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Transport is chosen by config: "Stdio" (default, local single client) or "Http" (hosted, multi-client).
var transport = Environment.GetEnvironmentVariable("Transport") ?? "Stdio";

if (string.Equals(transport, "Http", StringComparison.OrdinalIgnoreCase))
{
    var web = WebApplication.CreateBuilder(args);
    web.Logging.AddFilter("Microsoft", LogLevel.Warning);
    web.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
    ConfigureDomain(web.Services, web.Configuration);
    AddMcp(web.Services).WithHttpTransport().WithToolsFromAssembly();
    var app = web.Build();
    LogStartup(app.Services, web.Configuration, "Http");
    app.MapMcp();                 // exposes the Streamable HTTP MCP endpoint
    await app.RunAsync();
}
else
{
    var host = Host.CreateApplicationBuilder(args);
    // STDIO: logs MUST go to stderr, never stdout (stdout carries the JSON-RPC protocol).
    host.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    // Quiet the framework/SDK categories by default (override via Logging__LogLevel__*).
    host.Logging.AddFilter("Microsoft", LogLevel.Warning);
    host.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
    ConfigureDomain(host.Services, host.Configuration);
    AddMcp(host.Services).WithStdioServerTransport().WithToolsFromAssembly();
    var app = host.Build();
    LogStartup(app.Services, host.Configuration, "Stdio");
    await app.RunAsync();
}

// One concise startup line at Information; per-call tracing is at Debug (set
// Logging__LogLevel__Default=Debug). Logs go to stderr on the stdio transport.
static void LogStartup(IServiceProvider services, IConfiguration config, string transport)
{
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GarnetMcp");
    var provider = string.Equals(config["Embeddings:Provider"], "AzureOpenAI", StringComparison.OrdinalIgnoreCase)
        ? "AzureOpenAI" : "Fake";
    var garnetHost = string.IsNullOrWhiteSpace(config["Garnet:Host"]) ? "127.0.0.1" : config["Garnet:Host"];
    var garnetPort = int.TryParse(config["Garnet:Port"], out var p) ? p : 6379;
    logger.LogInformation(
        "Garnet memory MCP server started: transport={Transport}, embeddings={Provider}, garnet={Host}:{Port}",
        transport, provider, garnetHost, garnetPort);
}

// ---- shared configuration (same tools/services regardless of transport) ----

static void ConfigureDomain(IServiceCollection services, IConfiguration config)
{
    services.AddGarnetMemoryCore(
        keyPrefix: config["Garnet:KeyPrefix"] ?? "mem",
        logRedisCommands: string.Equals(config["Garnet:LogRedisCommands"], "true", StringComparison.OrdinalIgnoreCase));

    // Garnet connection: local/self-hosted OSS Garnet (single node or self-hosted cluster).
    // Treat blank/whitespace env values (e.g. an unfilled MCP client input) as "use the default".
    var garnetHost = config["Garnet:Host"];
    services.AddLocalGarnetConnection(new LocalGarnetOptions
    {
        Host = string.IsNullOrWhiteSpace(garnetHost) ? "127.0.0.1" : garnetHost,
        Port = int.TryParse(config["Garnet:Port"], out var port) ? port : 6379,
    });

    // Embedding provider.
    if (string.Equals(config["Embeddings:Provider"], "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
    {
        services.AddAzureOpenAIEmbeddings(new AzureOpenAIOptions
        {
            Endpoint = config["Embeddings:Endpoint"]
                ?? throw new InvalidOperationException("Embeddings:Endpoint is required when Embeddings:Provider=AzureOpenAI."),
            DeploymentName = config["Embeddings:DeploymentName"] ?? "text-embedding-3-small",
            Dimensions = int.TryParse(config["Embeddings:Dimensions"], out var dims) ? dims : 1536,
            ApiKey = config["Embeddings:ApiKey"],
        });
    }
    else
    {
        services.AddFakeEmbeddings(int.TryParse(config["Embeddings:Dimensions"], out var d) ? d : 8);
    }

    // Memory owner pinned from config so store and recall always agree across chats/sessions.
    services.AddSingleton(new MemoryToolsOptions { User = config["Memory:User"] ?? "default" });
}

static IMcpServerBuilder AddMcp(IServiceCollection services) => services.AddMcpServer(options =>
{
    options.ServerInstructions =
        "This server is the user's persistent long-term memory, backed by Garnet vector search.\n" +
        "- Before answering anything about the user (their preferences, facts, decisions, or past " +
        "statements), FIRST call recall_memory to check what is already known - even if you think you " +
        "know, because memories persist across sessions and are not in this conversation.\n" +
        "- Whenever the user states a durable fact about themselves or asks you to remember something, " +
        "call store_memory to persist it.";
});
