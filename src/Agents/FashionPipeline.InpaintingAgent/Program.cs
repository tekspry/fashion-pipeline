using A2A;
using A2A.AspNetCore;
using FashionPipeline.InpaintingAgent;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5501");

builder.Services.AddHttpClient("InpaintingMcp", client =>
{
    // OOTDiffusion HuggingFace inference can take 5+ minutes on cold start
    client.Timeout = TimeSpan.FromMinutes(8);
});

const string agentUrl = "http://localhost:5501";
var agentCard = new AgentCard
{
    Name = "FashionPipeline.InpaintingAgent",
    Description = "Applies exact accessory design (lace/border/trim) to a base model image using OOTDiffusion via HuggingFace Inference API.",
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
            Id = "apply_accessory",
            Name = "Apply Accessory to Dress",
            Description = "Accepts accessoryImageUri + baseModelImageUri and returns the final image URL with the accessory precisely applied via OOTDiffusion.",
            Tags = ["inpainting", "ootdiffusion", "virtual-tryon", "fashion"]
        }
    ]
};

builder.Services.AddA2AAgent<InpaintingAgentHandler>(agentCard);

var app = builder.Build();
app.MapA2A("/");
app.MapWellKnownAgentCard(agentCard);
app.Run();
