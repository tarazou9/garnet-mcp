using GarnetMcp.Core.Connection;
using GarnetMcp.Core.Embeddings;
using GarnetMcp.Core.Memory;
using GarnetMcp.Core.Vectors;

// Interactive demo: store text as memories and recall them by meaning, live.
// Uses Azure OpenAI embeddings when GARNET_AOAI_ENDPOINT is set (keyless via `az login`),
// otherwise falls back to the deterministic FakeEmbeddingProvider so it still runs offline.

string user = Environment.GetEnvironmentVariable("GARNET_DEMO_USER") ?? "demo";

IEmbeddingProvider embeddings;
var endpoint = Environment.GetEnvironmentVariable("GARNET_AOAI_ENDPOINT");
if (!string.IsNullOrEmpty(endpoint))
{
    var deployment = Environment.GetEnvironmentVariable("GARNET_AOAI_DEPLOYMENT") ?? "text-embedding-3-small";
    var dims = int.TryParse(Environment.GetEnvironmentVariable("GARNET_AOAI_DIMENSIONS"), out var d) ? d : 1536;
    embeddings = new AzureOpenAIEmbeddingProvider(new AzureOpenAIOptions
    {
        Endpoint = endpoint,
        DeploymentName = deployment,
        Dimensions = dims,
    });
    Console.WriteLine($"Embeddings: Azure OpenAI '{deployment}' ({dims} dims) at {endpoint}");
}
else
{
    embeddings = new FakeEmbeddingProvider(8);
    Console.WriteLine("Embeddings: FakeEmbeddingProvider (offline). Set GARNET_AOAI_ENDPOINT for real embeddings.");
}

await using var factory = new LocalGarnetConnectionFactory();
var vectors = new GarnetVectorClient(factory);
var store = new GarnetMemoryStore(vectors, embeddings);

Console.WriteLine($"User: {user}   Vector-set key: {store.Key}");
Console.WriteLine("""
    Commands:
      store <text>     - remember some text
      recall <query>   - find the most similar memories
      forget <id>      - delete a memory by id
      help             - show this help
      quit / exit      - leave
    """);

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null) break;
    line = line.Trim();
    if (line.Length == 0) continue;

    var space = line.IndexOf(' ');
    var cmd = (space < 0 ? line : line[..space]).ToLowerInvariant();
    var arg = space < 0 ? "" : line[(space + 1)..].Trim();

    try
    {
        switch (cmd)
        {
            case "quit" or "exit":
                return;

            case "help":
                Console.WriteLine("store <text> | recall <query> | forget <id> | quit");
                break;

            case "store":
                if (arg.Length == 0) { Console.WriteLine("usage: store <text>"); break; }
                var id = await store.StoreMemoryAsync(arg, user);
                Console.WriteLine($"  stored {id}");
                break;

            case "recall":
                if (arg.Length == 0) { Console.WriteLine("usage: recall <query>"); break; }
                var hits = await store.RecallMemoryAsync(arg, user, topK: 5);
                if (hits.Count == 0) { Console.WriteLine("  (no memories yet)"); break; }
                var rank = 1;
                foreach (var h in hits)
                {
                    var score = h.Score?.ToString("F4") ?? "n/a";
                    Console.WriteLine($"  {rank++}. [score {score}] {h.Text}   ({h.Id})");
                }
                break;

            case "forget":
                if (arg.Length == 0) { Console.WriteLine("usage: forget <id>"); break; }
                Console.WriteLine(await store.ForgetMemoryAsync(arg) ? "  forgotten" : "  no such memory");
                break;

            default:
                Console.WriteLine($"unknown command '{cmd}' (try: help)");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  error: {ex.Message}");
    }
}
