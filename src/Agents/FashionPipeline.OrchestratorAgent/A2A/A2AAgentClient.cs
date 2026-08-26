using A2A;
using FashionPipeline.Core.Jobs;
using Microsoft.Extensions.Options;

namespace FashionPipeline.OrchestratorAgent.A2A;

public sealed class A2AAgentClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentOptions _agentOptions;

    public A2AAgentClient(IHttpClientFactory httpClientFactory, IOptions<AgentOptions> agentOptions)
    {
        _httpClientFactory = httpClientFactory;
        _agentOptions = agentOptions.Value;
    }

    public Task<string> SendToVisionAsync(string payloadJson, CancellationToken cancellationToken) =>
        SendTextAsync(_agentOptions.VisionUrl, payloadJson, cancellationToken);

    public Task<string> SendToCreativeAsync(string featureJson, CancellationToken cancellationToken) =>
        SendTextAsync(_agentOptions.CreativeUrl, featureJson, cancellationToken);

    public Task<string> SendToImageAsync(string payloadJson, CancellationToken cancellationToken) =>
        SendTextAsync(_agentOptions.ImageUrl, payloadJson, cancellationToken);

    public Task<string> SendToVideoAsync(string payloadJson, CancellationToken cancellationToken) =>
        SendTextAsync(_agentOptions.VideoUrl, payloadJson, cancellationToken);

    public Task<string> SendToInpaintingAsync(string payloadJson, CancellationToken cancellationToken) =>
        SendTextAsync(_agentOptions.InpaintingUrl, payloadJson, cancellationToken);

    public async Task<string> SendTextAsync(string agentBaseUrl, string text, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient("A2AAgents");
        var client = new A2AClient(new Uri(agentBaseUrl.TrimEnd('/') + "/"), http);

        var response = await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Role.User,
                Parts = [Part.FromText(text)]
            }
        }, cancellationToken);

        var responseText = ExtractResponseText(response);
        
        // If the agent returned our structured error payload, throw it so the orchestrator halts
        if (responseText.Contains("\"error\":true") && responseText.Contains("\"message\":"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(responseText);
                if (doc.RootElement.TryGetProperty("error", out var isErr) && isErr.GetBoolean())
                {
                    var msg = doc.RootElement.GetProperty("message").GetString();
                    throw new InvalidOperationException($"Agent {agentBaseUrl} failed: {msg}");
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON, ignore
            }
        }

        return responseText;
    }

    public static string ExtractResponseText(SendMessageResponse response)
    {
        if (response.PayloadCase == SendMessageResponseCase.Message)
            return response.Message?.Parts?.FirstOrDefault()?.Text ?? string.Empty;

        if (response.PayloadCase == SendMessageResponseCase.Task)
        {
            var agentMessage = response.Task?.History?
                .LastOrDefault(m => m.Role == Role.Agent);

            if (agentMessage is not null)
                return agentMessage.Parts?.FirstOrDefault()?.Text ?? string.Empty;

            return response.Task?.Status?.Message?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
        }

        return string.Empty;
    }
}