using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Embeddings;

namespace GarnetMcp.Core.Embeddings;

/// <summary>Options for the Azure OpenAI embedding provider.</summary>
public sealed class AzureOpenAIOptions
{
    /// <summary>Resource endpoint, e.g. https://my-aoai.openai.azure.com/.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Embedding deployment name, e.g. "text-embedding-3-small".</summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>Vector length the model produces, e.g. 1536.</summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>Optional API key. Leave null/empty to use Entra auth (DefaultAzureCredential) — preferred.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Real embeddings via Azure OpenAI. Uses <see cref="DefaultAzureCredential"/> (keyless, preferred)
/// when no API key is supplied — locally that is your <c>az login</c> identity; hosted it is the
/// app's managed identity with the "Cognitive Services OpenAI User" role.
/// </summary>
public sealed class AzureOpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingClient _client;

    public string ModelName { get; }
    public int Dimensions { get; }

    public AzureOpenAIEmbeddingProvider(AzureOpenAIOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new ArgumentException("Azure OpenAI endpoint is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.DeploymentName))
            throw new ArgumentException("Azure OpenAI deployment name is required.", nameof(options));

        ModelName = options.DeploymentName;
        Dimensions = options.Dimensions;

        var uri = new Uri(options.Endpoint);
        var azure = string.IsNullOrEmpty(options.ApiKey)
            ? new AzureOpenAIClient(uri, new DefaultAzureCredential())
            : new AzureOpenAIClient(uri, new ApiKeyCredential(options.ApiKey));
        _client = azure.GetEmbeddingClient(options.DeploymentName);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value.ToFloats().ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var result = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value.Select(e => e.ToFloats().ToArray()).ToList();
    }
}
