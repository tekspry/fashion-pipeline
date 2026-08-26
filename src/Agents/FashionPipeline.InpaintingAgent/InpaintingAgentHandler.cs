using System.Text.Json;
using A2A;
using FashionPipeline.Agents.Mcp;

namespace FashionPipeline.InpaintingAgent;

public sealed class InpaintingAgentHandler : IAgentHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InpaintingAgentHandler> _logger;

    public InpaintingAgentHandler(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<InpaintingAgentHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);
        var text = context.UserText ?? string.Empty;

        // OOTDiffusion on HF can take up to 5 minutes on cold start
        using var mcpCts = new CancellationTokenSource(TimeSpan.FromMinutes(8));

        try
        {
            var payload = AgentPayloadParser.ParseInpaintingRequest(text)
                ?? throw new ArgumentException(
                    "InpaintingAgent expects {\"accessoryImageUri\":\"...\",\"baseModelImageUri\":\"...\"} JSON payload.");

            _logger.LogInformation(
                "InpaintingAgent: Applying accessory '{Accessory}' to base model image '{Model}'",
                payload.AccessoryImageUri, payload.BaseModelImageUri);

            var http = _httpClientFactory.CreateClient("InpaintingMcp");
            var mcpBaseUrl = _configuration["McpServers:InpaintingUrl"]
                ?? throw new InvalidOperationException("McpServers:InpaintingUrl is not configured.");

            var mcp = new McpJsonRpcClient(http, mcpBaseUrl);
            var finalImageUrl = await mcp.CallToolAsync(
                "apply_accessory_to_dress",
                new
                {
                    accessoryImageUri = payload.AccessoryImageUri,
                    baseModelImageUri = payload.BaseModelImageUri
                },
                mcpCts.Token);

            _logger.LogInformation("InpaintingAgent: Final image generated at {Url}", finalImageUrl);

            using var replyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await responder.ReplyAsync(finalImageUrl, replyCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InpaintingAgentHandler failed processing message: {Text}", text);
            var errorPayload = JsonSerializer.Serialize(new
            {
                error = true,
                message = ex.Message,
                type = ex.GetType().Name
            });

            using var errorReplyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await responder.ReplyAsync(errorPayload, errorReplyCts.Token);
            }
            catch (Exception replyEx)
            {
                _logger.LogError(replyEx, "InpaintingAgentHandler failed to send error reply");
            }
        }
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
