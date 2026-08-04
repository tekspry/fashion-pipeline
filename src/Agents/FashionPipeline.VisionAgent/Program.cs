using A2A;
using A2A.AspNetCore;
using FashionPipeline.VisionAgent;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5101");

builder.Services.AddHttpClient("VisionMcp", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10); // Gemini vision can take 2-4 min
});

const string agentUrl = "http://localhost:5101";
var agentCard = new AgentCard
{
    Name = "FashionPipeline.VisionAgent",
    Description = "Extracts fashion accessory features from an image URL via Vision MCP (Gemini Vision).",
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
            Id = "extract_features",
            Name = "Extract Accessory Features",
            Description = "Accepts an imageUrl and returns JSON features (Color, Type, Material, Vibe, Style).",
            Tags = ["vision", "features", "gemini"]
        }
    ]
};

builder.Services.AddA2AAgent<VisionAgentHandler>(agentCard);

var app = builder.Build();
app.MapA2A("/");
app.MapWellKnownAgentCard(agentCard);
app.Run();