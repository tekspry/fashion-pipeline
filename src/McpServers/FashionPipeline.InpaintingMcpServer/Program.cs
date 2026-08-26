using System.Reflection;
using FastMCP.Hosting;
using FastMCP.Server;
using FashionPipeline.InpaintingMcpServer;

var mcpServer = new FastMCPServer(name: "FashionPipeline.InpaintingMcpServer");
var builder = McpServerBuilder.Create(mcpServer, args);
builder.WithComponentsFrom(Assembly.GetExecutingAssembly());

// VTON options (Default: 100% Free Hugging Face Gradio Space API)
builder.Services.AddOptions<VtonOptions>().BindConfiguration("Vton");
builder.Services.AddOptions<ReplicateOptions>().BindConfiguration("Replicate");
builder.Services.AddOptions<ImagenEditOptions>().BindConfiguration("ImagenEdit");

builder.Services.AddHttpClient<InpaintingTool>(client =>
{
    // Generous timeout for free Gradio ZeroGPU inference
    client.Timeout = TimeSpan.FromMinutes(8);
});

var app = builder.Build();
await app.RunMcpAsync(args);
