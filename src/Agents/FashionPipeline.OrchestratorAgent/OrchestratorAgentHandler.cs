using A2A;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FashionPipeline.OrchestratorAgent;

public sealed class OrchestratorAgentHandler : IAgentHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrchestratorAgentHandler> _logger;

    public OrchestratorAgentHandler(IServiceScopeFactory scopeFactory, ILogger<OrchestratorAgentHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);

        // Use an INDEPENDENT timeout CT for pipeline work.
        // The parent 'cancellationToken' is HttpContext.RequestAborted from the PipelineAgentJob's
        // HTTP connection — if that drops (e.g. Hangfire job timeout), it would cancel all downstream
        // A2A and MCP calls mid-flight, producing SocketException 995 cascades.
        using var pipelineCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<OrchestratorPipelineHandler>();
            var result = await pipeline.RunAsync(context.UserText ?? string.Empty, pipelineCts.Token);

            // Reply using an independent short-lived CT so it doesn't fail if the incoming HTTP request context was marked aborted
            using var replyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await responder.ReplyAsync(result, replyCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrchestratorAgentHandler pipeline failed");
            var errorPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = true,
                message = ex.Message,
                type = ex.GetType().Name
            });

            // Use a fresh CT for error reply — parent CT may already be cancelled
            using var errorReplyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await responder.ReplyAsync(errorPayload, errorReplyCts.Token);
            }
            catch (Exception replyEx)
            {
                _logger.LogError(replyEx, "OrchestratorAgentHandler failed to send error reply");
            }
        }
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}