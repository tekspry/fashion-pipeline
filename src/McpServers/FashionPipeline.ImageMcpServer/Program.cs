using System.Reflection;
using FastMCP.Hosting;
using FastMCP.Server;
using FashionPipeline.ImageMcpServer;

var mcpServer = new FastMCPServer(name: "FashionPipeline.ImageMcpServer");
var builder = McpServerBuilder.Create(mcpServer, args);
builder.WithComponentsFrom(Assembly.GetExecutingAssembly());

builder.Services.AddOptions<ImagenOptions>().BindConfiguration("Imagen");
builder.Services.AddOptions<FashionPipeline.Core.Options.AiProviderOptions>().BindConfiguration("AIProvider");
builder.Services.AddOptions<StorageOptions>().BindConfiguration("Storage");
builder.Services.AddHttpClient<ImageGenerationTool>();

var app = builder.Build();
await app.RunMcpAsync(args);
