using System.Text.Json;
using A2A;
using FashionPipeline.Agents.Mcp;

namespace FashionPipeline.ImageAgent;

public sealed class ImageAgentHandler : IAgentHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImageAgentHandler> _logger;

    public ImageAgentHandler(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ImageAgentHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);
        var text = context.UserText ?? string.Empty;

        using var mcpCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            var payload = AgentPayloadParser.ParseImageRequest(text)
                ?? throw new ArgumentException(
                    "ImageAgent expects {\"prompt\":\"...\",\"rawImageUri\":\"...\"} " +
                    "(imageUrl is also accepted and mapped to rawImageUri).");

            var http = _httpClientFactory.CreateClient("ImageMcp");
            var mcpBaseUrl = _configuration["McpServers:ImageUrl"]
                ?? throw new InvalidOperationException("McpServers:ImageUrl is not configured.");

            var mcp = new McpJsonRpcClient(http, mcpBaseUrl);
            var assetUrl = await mcp.CallToolAsync(
                "generate_accessory_image",
                new { prompt = payload.Prompt, rawImageUri = payload.RawImageUri },
                mcpCts.Token);

            using var replyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await responder.ReplyAsync(assetUrl, replyCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ImageAgentHandler failed processing message: {Text}", text);
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
                _logger.LogError(replyEx, "ImageAgentHandler failed to send error reply");
            }
        }
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}