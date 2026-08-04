using A2A;
using A2A.AspNetCore;
using FashionPipeline.CreativeAgent;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5201");

builder.Services.AddHttpClient("PromptMcp", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10); // Gemini multimodal prompt can take 2-4 min
});


const string agentUrl = "http://localhost:5201";
var agentCard = new AgentCard
{
    Name = "FashionPipeline.CreativeAgent",
    Description = "Generates cinematic image prompts from extracted accessory features via Prompt MCP.",
    Version = "1.0.0",
    SupportedInterfaces =
    [
        new AgentInterface
        {
            Url = agentUrl,
            ProtocolBinding = "JSONRPC",
            ProtocolVersion = "1.0"
        }
    ],
    DefaultInputModes = ["text/plain"],
    DefaultOutputModes = ["text/plain"],
    Capabilities = new AgentCapabilities { Streaming = false },
    Skills =
    [
        new AgentSkill
        {
            Id = "generate_prompts",
            Name = "Generate Image Prompts",
            Description = "Accepts featureJson and returns a JSON array of cinematic prompt strings.",
            Tags = ["creative", "prompts", "templates"]
        }
    ]
};

builder.Services.AddA2AAgent<CreativeAgentHandler>(agentCard);

var app = builder.Build();
app.MapA2A("/");
app.MapWellKnownAgentCard(agentCard);
app.Run();