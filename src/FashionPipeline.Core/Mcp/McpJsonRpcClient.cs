using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FashionPipeline.Agents.Mcp;

/// <summary>
/// Raw JSON-RPC client for DotnetFastMCP HTTP transport (POST /mcp).
/// Does not reference the FastMCP NuGet package.
/// </summary>
public sealed class McpJsonRpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _mcpEndpoint;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private int _requestId;
    private bool _initialized;

    public McpJsonRpcClient(HttpClient httpClient, string mcpBaseUrl)
    {
        _http = httpClient;
        _mcpEndpoint = mcpBaseUrl.TrimEnd('/') + "/mcp";
    }

    public async Task<string> CallToolAsync(
        string toolName,
        object arguments,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var doc = await PostRpcAsync(
            "tools/call",
            new { name = toolName, arguments },
            cancellationToken);

        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var msg)
                ? msg.GetString()
                : error.ToString();
            throw new InvalidOperationException($"MCP tools/call error: {message}");
        }

        if (!doc.RootElement.TryGetProperty("result", out var result))
            throw new InvalidOperationException("MCP response missing 'result'.");

        if (result.TryGetProperty("isError", out var isErrorEl) && isErrorEl.GetBoolean())
        {
            var errText = ExtractFirstTextContent(result);
            throw new InvalidOperationException($"MCP tool error: {errText}");
        }

        return ExtractFirstTextContent(result);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            using var initDoc = await PostRpcAsync(
                "initialize",
                new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "FashionPipeline.Agent", version = "1.0.0" }
                },
                cancellationToken);

            if (initDoc.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var msg)
                    ? msg.GetString()
                    : error.ToString();
                throw new InvalidOperationException($"MCP initialize error: {message}");
            }

            // MCP spec: client notifies server after initialize (stateless server accepts it)
            await PostNotificationAsync("notifications/initialized", null, cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<JsonDocument> PostRpcAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        using var response = await _http.PostAsJsonAsync(_mcpEndpoint, request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task PostNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        using var response = await _http.PostAsJsonAsync(_mcpEndpoint, request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        // Response body is an ack; no need to parse for stateless HTTP transport
        await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string ExtractFirstTextContent(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array ||
            content.GetArrayLength() == 0)
        {
            return result.GetRawText();
        }

        var first = content[0];
        if (first.TryGetProperty("text", out var textEl))
            return textEl.GetString() ?? string.Empty;

        return first.GetRawText();
    }
}