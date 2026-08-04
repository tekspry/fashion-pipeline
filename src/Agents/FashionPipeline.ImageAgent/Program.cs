using A2A;
using A2A.AspNetCore;
using FashionPipeline.ImageAgent;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5301");

builder.Services.AddHttpClient("ImageMcp", client =>
{
    client.Timeout = TimeSpan.FromMinutes(6); // Imagen generation can be slow
});


const string agentUrl = "http://localhost:5301";
var agentCard = new AgentCard
{
    Name = "FashionPipeline.ImageAgent",
    Description = "Generates accessory images via Imagen 3 MCP and stores them in Azure Blob.",
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
            Id = "generate_image",
            Name = "Generate Accessory Image",
            Description = "Accepts prompt + rawImageUri and returns the generated image blob URL.",
            Tags = ["image", "imagen", "media"]
        }
    ]
};

builder.Services.AddA2AAgent<ImageAgentHandler>(agentCard);

var app = builder.Build();
app.MapA2A("/");
app.MapWellKnownAgentCard(agentCard);
app.Run();