using A2A;
using A2A.AspNetCore;
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Jobs;
using FashionPipeline.Core.Services;
using FashionPipeline.OrchestratorAgent;
using FashionPipeline.OrchestratorAgent.A2A;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5050");

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient("A2AAgents", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

builder.Services.AddScoped<ICatalogPublishService, CatalogPublishService>();
builder.Services.AddScoped<A2AAgentClient>();
builder.Services.AddScoped<OrchestratorPipelineHandler>();

const string agentUrl = "http://localhost:5050";
var agentCard = new AgentCard
{
    Name = "FashionPipeline.OrchestratorAgent",
    Description = "Coordinates Vision, Creative, Image, and Video agents for one accessory end-to-end.",
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
            Id = "process_accessory",
            Name = "Process Accessory Pipeline",
            Description = "Runs feature extraction, prompt generation, image loop, and video generation.",
            Tags = ["orchestrator", "pipeline"]
        }
    ]
};

builder.Services.AddA2AAgent<OrchestratorAgentHandler>(agentCard);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000;");
}

app.MapA2A("/");
app.MapWellKnownAgentCard(agentCard);
app.Run();