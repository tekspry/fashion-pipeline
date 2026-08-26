using System.Reflection;
using FastMCP.Hosting;
using FastMCP.Server;
using FashionPipeline.PromptMcpServer;

var mcpServer = new FastMCPServer(name: "FashionPipeline.PromptMcpServer");
var builder = McpServerBuilder.Create(mcpServer, args);
builder.WithComponentsFrom(Assembly.GetExecutingAssembly());

builder.Services.AddOptions<GeminiOptions>().BindConfiguration("Gemini");
builder.Services.AddOptions<FashionPipeline.Core.Options.AiProviderOptions>().BindConfiguration("AIProvider");
builder.Services.AddHttpClient<PromptGenerationTool>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});


var app = builder.Build();
await app.RunMcpAsync(args);