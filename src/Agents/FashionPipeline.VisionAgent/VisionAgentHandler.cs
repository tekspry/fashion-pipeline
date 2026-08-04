using A2A;
using FashionPipeline.Agents.Mcp;

namespace FashionPipeline.VisionAgent;

public sealed class VisionAgentHandler : IAgentHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VisionAgentHandler> _logger;

    public VisionAgentHandler(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<VisionAgentHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);
        var text = context.UserText ?? string.Empty;

        // Use an INDEPENDENT timeout CancellationToken for the MCP call.
        // NEVER use the parent 'cancellationToken' (= HttpContext.RequestAborted) for
        // downstream work — it fires when the A2A caller drops the HTTP connection,
        // which would cancel the Gemini call prematurely and produce SocketException 995.
        using var mcpCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            var imageUrl = AgentPayloadParser.GetString(text, "imageUrl")
                ?? throw new ArgumentException(
                    "VisionAgent expects {\"imageUrl\":\"...\"} or a plain image URL string.");

            var http = _httpClientFactory.CreateClient("VisionMcp");
            var mcpBaseUrl = _configuration["McpServers:VisionUrl"]
                ?? throw new InvalidOperationException("McpServers:VisionUrl is not configured.");

            var mcp = new McpJsonRpcClient(http, mcpBaseUrl);

            var featureJson = await mcp.CallToolAsync(
                "extract_accessory_features",
                new { imageUrl },
                mcpCts.Token);  // ← Independent CT, not parent cancellationToken

            // Reply back using an independent short-lived CT so it doesn't fail if incoming HTTP context was marked aborted
            using var replyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await responder.ReplyAsync(featureJson, replyCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VisionAgentHandler failed processing message: {Text}", text);

            var errorPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = true,
                message = ex.Message,
                type = ex.GetType().Name
            });

            // Use a fresh independent CT for replying with error — parent CT may already be cancelled
            using var errorReplyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await responder.ReplyAsync(errorPayload, errorReplyCts.Token);
            }
            catch (Exception replyEx)
            {
                _logger.LogError(replyEx, "VisionAgentHandler failed to send error reply");
            }
        }
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
