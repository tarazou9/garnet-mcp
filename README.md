<!-- mcp-name: io.github.tarazou9/garnet-mcp -->

# Garnet Vector Memory MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io) server that gives LLM agents a **retrieval-augmented (RAG) memory** backed by **[Garnet](https://github.com/microsoft/garnet) Vector Sets**. Agents store text as embeddings and later retrieve the most semantically relevant snippets by meaning to ground their responses — persisting across sessions, not just within a single conversation. This is application-layer semantic recall (RAG): it augments the model's prompt with retrieved context, and is distinct from inference-layer KV-cache reuse (e.g. LMCache). It runs against a **local or self-hosted OSS Garnet** (or any Redis-compatible endpoint that supports Vector Sets), with bring-your-own embeddings.

## Tools

The server exposes five tools over the MCP `stdio` transport (HTTP is also supported):

| Tool | Description |
| ---- | ----------- |
| `store_memory` | Persist a fact or piece of text to long-term memory. Returns a memory id. |
| `recall_memory` | Semantic search over stored memories; returns the closest matches with scores. |
| `forget_memory` | Delete a stored memory by its id. |
| `list_indexes` | List the vector-set memory indexes that currently exist. |
| `index_info` | Show metadata (metric, dimensions, size) for a memory index key. |

## Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet)** — required so clients can launch the server with the `dnx` command (ships with the .NET 10 SDK). The package itself targets `net9.0`.
- **A Garnet server started with Vector Sets enabled.** Vector Sets are currently a Garnet **preview** feature and must be turned on explicitly with `--enable-vector-set-preview`. Any Redis-compatible endpoint exposing the `V*` vector commands also works.
- **Embeddings (optional but recommended).** Set `Embeddings__Provider=AzureOpenAI` and point it at an Azure OpenAI embedding deployment (keyless auth via `DefaultAzureCredential` / `az login`). Without it, the server falls back to a deterministic **Fake** provider that requires no external calls — useful for local demos and tests, but not for meaningful semantic recall.

## Install (from NuGet)

The server is published as a .NET tool package that MCP clients run via `dnx`. Add it to your client's MCP config, e.g. `.vscode/mcp.json` for VS Code / GitHub Copilot:

```json
{
  "servers": {
    "garnet-mcp": {
      "type": "stdio",
      "command": "dnx",
      "args": ["GarnetMcp.Server@0.1.0", "--yes"],
      "env": {
        "Garnet__Host": "127.0.0.1",
        "Garnet__Port": "6379",
        "Embeddings__Provider": "AzureOpenAI",
        "Embeddings__Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
        "Embeddings__DeploymentName": "text-embedding-3-small",
        "Embeddings__Dimensions": "1536"
      }
    }
  }
}
```

To run the fully offline demo, drop the `Embeddings__*` entries (or set `Embeddings__Provider=Fake`).

## Recommended agent instructions

The server already sends usage guidance to any MCP client at connect time (via the server's `ServerInstructions`), so most agents will use the tools appropriately on their own. If you want to make an agent use the memory proactively, add something like the following to that agent's custom-instructions (e.g. `.github/copilot-instructions.md` in *your* project, a Claude/Cursor rules file, etc.):

> Before answering anything about the user — their preferences, facts, decisions, or past statements — FIRST call `recall_memory`, even if the answer seems to be in the current chat (memories persist across sessions). Whenever the user states a durable fact about themselves or asks you to remember something, call `store_memory` to persist it, then briefly confirm what you saved.

## Configuration

All settings are read from configuration / environment variables. In environment-variable form, use `__` (double underscore) as the section separator.

| Setting | Env var | Default | Notes |
| ------- | ------- | ------- | ----- |
| Garnet host | `Garnet__Host` | `127.0.0.1` | Blank is treated as the default. |
| Garnet port | `Garnet__Port` | `6379` | |
| Key prefix | `Garnet__KeyPrefix` | `mem` | Prefix for memory index keys. |
| Log Redis commands | `Garnet__LogRedisCommands` | `false` | Set `true` to log the equivalent redis-cli command at `Debug` (troubleshooting). |
| Log level | `Logging__LogLevel__Default` | `Information` | Default is a quiet startup line + warnings/errors. Set `Debug` for per-call tool tracing. Logs go to **stderr** on the stdio transport. |
| Embedding provider | `Embeddings__Provider` | `Fake` | `AzureOpenAI` or `Fake`. |
| Azure OpenAI endpoint | `Embeddings__Endpoint` | — | Required when provider is `AzureOpenAI`. |
| Deployment name | `Embeddings__DeploymentName` | `text-embedding-3-small` | |
| Dimensions | `Embeddings__Dimensions` | `1536` (AOAI) / `8` (Fake) | Must match the embedding model. |
| API key | `Embeddings__ApiKey` | — | Optional; prefer keyless `DefaultAzureCredential`. |
| Memory owner | `Memory__User` | `default` | Pins the memory owner so store and recall always agree. |
| Transport | `Transport` | `Stdio` | `Stdio` (local) or `Http` (hosted). |

## Embedding providers

Two providers ship out of the box:

- **`AzureOpenAI`** — real embeddings; the only production-quality option. Keyless via `DefaultAzureCredential` (or an API key).
- **`Fake`** — deterministic, offline, hash-seeded vectors. Useful for local runs and tests, but **not semantically meaningful** — don't use it for real recall.

Embedding generation sits behind the `IEmbeddingProvider` abstraction (`EmbedAsync` / `EmbedBatchAsync`, plus `ModelName` and `Dimensions`). The rest of the system — `GarnetMemoryStore`, the vector client, and the tools — depends solely on this interface, so additional backends (for example OpenAI, a local model served via Ollama, or Hugging Face) can be introduced without touching that code. To add one, implement `IEmbeddingProvider` and register it in `Program.cs` (`ConfigureDomain`) under the corresponding `Embeddings__Provider` value.

## Memory ownership (single-tenant by design)

The memory owner is pinned by configuration (`Memory__User`, default `default`) for the lifetime of the server process — the model cannot choose it per call. Both `store_memory` and `recall_memory` are scoped to that owner (recall filters on the stored `user` attribute), so store and recall always agree across chats and sessions.

This suits the shipped model: a personal, single-owner server launched over stdio by your own client — the process *is* the user. Letting the model supply the user per call is deliberately avoided because it would let store and recall drift apart (models are inconsistent) and would let one caller read or overwrite another owner's memories.

For a hosted, multi-tenant deployment (the `Http` transport serving many people), the correct source of the owner is the **authenticated caller's identity** (from the auth token/headers) — **never** a value chosen by the model. That is a deliberate future enhancement, not something to wire through the tool arguments.

## Build and run from source

```bash
# Build + test the solution (integration tests self-skip when Garnet/AOAI are unreachable).
dotnet test src/GarnetMcp.slnx

# Run the server directly over stdio.
dotnet run --project src/GarnetMcp.Server

# Optional: interactive console to try store/recall/forget by hand (dev only; not part of the package).
dotnet run --project src/GarnetMcp.Demo
```

Start a local Garnet with Vector Sets enabled before exercising the memory tools:

```bash
garnet --enable-vector-set-preview true
```

## Caveats

- **Preview MCP SDK.** This project pins `ModelContextProtocol 2.0.0-preview.2`. The SDK's API surface may change between preview releases.
- **Garnet Vector Sets are preview.** Start Garnet with `--enable-vector-set-preview`, or memory tools return a clear "Vector Sets are not enabled" error.
- **`list_indexes` on a multi-node cluster.** Index enumeration uses `SCAN` on the connected node, so against a self-hosted multi-node Garnet cluster it returns only that node's keys (partial results). Single-node / self-hosted setups are unaffected, and `store_memory` / `recall_memory` / `forget_memory` are unaffected either way (they operate on specific keys).

## License

[MIT](LICENSE) © Tara Zou
