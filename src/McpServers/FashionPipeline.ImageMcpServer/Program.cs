using System.Reflection;
using FastMCP.Hosting;
using FastMCP.Server;
using FashionPipeline.ImageMcpServer;

var mcpServer = new FastMCPServer(name: "FashionPipeline.ImageMcpServer");
var builder = McpServerBuilder.Create(mcpServer, args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.WithComponentsFrom(Assembly.GetExecutingAssembly());

builder.Services.AddOptions<ImagenOptions>().BindConfiguration("Imagen");
builder.Services.AddOptions<StorageOptions>().BindConfiguration("Storage");
builder.Services.AddHttpClient<ImageGenerationTool>();

var app = builder.Build();
await app.RunMcpAsync(args);
