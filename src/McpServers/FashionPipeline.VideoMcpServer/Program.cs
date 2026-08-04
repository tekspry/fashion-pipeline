using System.Reflection;
using FastMCP.Hosting;
using FastMCP.Server;
using FashionPipeline.VideoMcpServer;

var mcpServer = new FastMCPServer(name: "FashionPipeline.VideoMcpServer");
var builder = McpServerBuilder.Create(mcpServer, args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.WithComponentsFrom(Assembly.GetExecutingAssembly());

builder.Services.AddOptions<KlingOptions>().BindConfiguration("Kling");
builder.Services.AddOptions<StorageOptions>().BindConfiguration("Storage");
builder.Services.AddOptions<VideoApiOptions>().BindConfiguration(VideoApiOptions.SectionName);
builder.Services.AddHttpClient<VideoGenerationTool>();

// Required by VideoGenerationTool constructor for tracking API Quotas:
builder.Services.AddMemoryCache(); 

var app = builder.Build();
await app.RunMcpAsync(args);