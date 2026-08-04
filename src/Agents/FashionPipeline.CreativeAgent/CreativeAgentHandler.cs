using A2A;
using FashionPipeline.Agents.Mcp;

namespace FashionPipeline.CreativeAgent;

public sealed class CreativeAgentHandler : IAgentHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CreativeAgentHandler> _logger;

    public CreativeAgentHandler(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<CreativeAgentHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    // public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    // {
    //     var responder = new MessageResponder(eventQueue, context.ContextId);
    //     var text = context.UserText ?? string.Empty;

    //     try
    //     {
    //         var featureJson = AgentPayloadParser.GetFeatureJson(text)
    //             ?? throw new ArgumentException(
    //                 "CreativeAgent expects {\"featureJson\":\"...\"} or raw feature JSON with Color/Type keys.");

    //         var imageUrl = AgentPayloadParser.GetImageUrl(text)
    //             ?? throw new ArgumentException("CreativeAgent expects {\"imageUrl\":\"...\"}.");

    //         var http = _httpClientFactory.CreateClient("PromptMcp");
    //         var mcpBaseUrl = _configuration["McpServers:PromptUrl"]
    //             ?? throw new InvalidOperationException("McpServers:PromptUrl is not configured.");

    //         var mcp = new McpJsonRpcClient(http, mcpBaseUrl);
    //         var promptsJson = await mcp.CallToolAsync(
    //             "generate_image_prompts",
    //             new { featureJson, imageUrl },
    //             cancellationToken);

    //         await responder.ReplyAsync(promptsJson, cancellationToken);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "CreativeAgentHandler failed processing message: {Text}", text);
    //         var errorPayload = System.Text.Json.JsonSerializer.Serialize(new
    //         {
    //             error = true,
    //             message = ex.Message,
    //             type = ex.GetType().Name
    //         });
    //         await responder.ReplyAsync(errorPayload, cancellationToken);
    //     }
    // }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);
        var text = context.UserText ?? string.Empty;

        // Independent 10-minute timeout for Gemini Prompt MCP generation
        using var mcpCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            var featureJson = AgentPayloadParser.GetFeatureJson(text)
                ?? throw new ArgumentException(
                    "CreativeAgent expects {\"featureJson\":\"...\"} or raw feature JSON with Color/Type keys.");

            var imageUrl = AgentPayloadParser.GetImageUrl(text)
                ?? throw new ArgumentException("CreativeAgent expects {\"imageUrl\":\"...\"}.");

            var http = _httpClientFactory.CreateClient("PromptMcp");
            var mcpBaseUrl = _configuration["McpServers:PromptUrl"]
                ?? throw new InvalidOperationException("McpServers:PromptUrl is not configured.");

            var mcp = new McpJsonRpcClient(http, mcpBaseUrl);
            var promptsJson = await mcp.CallToolAsync(
                "generate_image_prompts",
                new { featureJson, imageUrl },
                mcpCts.Token);

            // Independent 30-second CT for A2A reply
            using var replyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await responder.ReplyAsync(promptsJson, replyCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreativeAgentHandler failed processing message: {Text}", text);
            var errorPayload = System.Text.Json.JsonSerializer.Serialize(new
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
                _logger.LogError(replyEx, "CreativeAgentHandler failed to send error reply");
            }
        }
    }

    
    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}