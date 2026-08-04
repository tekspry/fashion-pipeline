using System.Text.Json;
using A2A;
using FashionPipeline.Agents.Mcp;

namespace FashionPipeline.VideoAgent;

public sealed class VideoAgentHandler : IAgentHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VideoAgentHandler> _logger;

    public VideoAgentHandler(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<VideoAgentHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);
        var text = context.UserText ?? string.Empty;

        try
        {
            var imageUrl = AgentPayloadParser.GetString(text, "imageUrl")
                ?? throw new ArgumentException("VideoAgent expects {\"imageUrl\":\"...\"}.");

            var http = _httpClientFactory.CreateClient("VideoMcp");
            var mcpBaseUrl = _configuration["McpServers:VideoUrl"]
                ?? throw new InvalidOperationException("McpServers:VideoUrl is not configured.");

            var mcp = new McpJsonRpcClient(http, mcpBaseUrl);
            var result = await mcp.CallToolAsync(
                "generate_accessory_video",
                new { imageUrl },
                cancellationToken);

            await responder.ReplyAsync(result.Trim(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VideoAgentHandler failed processing message: {Text}", text);
            var errorPayload = JsonSerializer.Serialize(new
            {
                error = true,
                message = ex.Message,
                type = ex.GetType().Name
            });
            await responder.ReplyAsync(errorPayload, cancellationToken);
        }
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}