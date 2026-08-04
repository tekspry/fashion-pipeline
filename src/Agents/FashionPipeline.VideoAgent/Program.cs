using A2A;
using A2A.AspNetCore;
using FashionPipeline.VideoAgent;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5401");

builder.Services.AddHttpClient("VideoMcp", client =>
{
    client.Timeout = TimeSpan.FromMinutes(6);
});


const string agentUrl = "http://localhost:5401";
var agentCard = new AgentCard
{
    Name = "FashionPipeline.VideoAgent",
    Description = "Generates promotional accessory videos via Video MCP (Kling AI).",
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
            Id = "generate_video",
            Name = "Generate Accessory Video",
            Description = "Accepts imageUrl and returns blob URL or QUOTA_EXHAUSTED.",
            Tags = ["video", "kling", "media"]
        }
    ]
};

builder.Services.AddA2AAgent<VideoAgentHandler>(agentCard);

var app = builder.Build();
app.MapA2A("/");
app.MapWellKnownAgentCard(agentCard);
app.Run();