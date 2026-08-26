# Phase 1 Code Implementation Plan

This document is the authoritative implementation guide for Phase 1. Each section is explicitly marked with its change status.

## Change Summary

| Section | Status | What Changed |
|---|---|---|
| 1. Domain & DB Layer | ✅ **UNCHANGED** | Entities, DbContext, PromptOptions identical |
| 2. FastMCP Tools | 🔴 **REPLACED** | Moved to separate MCP server projects; all mocks replaced with real API calls |
| 3. Orchestration | 🔴 **REPLACED** | In-process SK AgentGroupChat replaced with real A2A HTTP client |
| 4. API & Configuration | 🟡 **UPDATED** | Added agent/MCP server URLs; storage options added |
| 5A. New: MCP Server Projects | 🔴 **NEW** | 4 separate DotnetFastMCP server projects, one per domain |
| 5B. New: Agent Projects | 🔴 **NEW** | 5 separate A2A-compliant agent services |
| 6. Testing Strategy | ✅ **UNCHANGED** | Same layered unit/snapshot/integration/E2E tests |
| 7. Developer Documentation | ✅ **UNCHANGED** | developer_guide.md stays as is |

> [!IMPORTANT]
> Only the items marked 🔴 **REPLACED** or 🔴 **NEW** require code changes. Items marked ✅ **UNCHANGED** already exist correctly in your `src/` folder — do not modify them.

---

## 1. Domain & Database Layer

Create the following files in the `FashionPipeline.Core/Entities` and `FashionPipeline.Core/Data` folders.

### `FashionPipeline.Core/Entities/Accessory.cs`
```csharp
using System;

namespace FashionPipeline.Core.Entities;

public enum AccessoryStatus
{
    Pending, Processing, VideoPending, RequiresManualVideo, Complete, Failed
}

public class Accessory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } // SaaS Isolation
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RawImageUri { get; set; } = string.Empty;
    public string ImageHash { get; set; } = string.Empty; // For bypassing Gemini extraction
    public string? ExtractedFeatures { get; set; }
    public AccessoryStatus Status { get; set; } = AccessoryStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### `FashionPipeline.Core/Entities/GeneratedAsset.cs`
```csharp
using System;

namespace FashionPipeline.Core.Entities;

public class GeneratedAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } // SaaS Isolation
    public Guid AccessoryId { get; set; }
    public string AssetType { get; set; } = string.Empty; // "Image" or "Video"
    public string AssetUri { get; set; } = string.Empty;
    public string PromptUsed { get; set; } = string.Empty;
    public bool IsApproved { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### `FashionPipeline.Core/Data/AppDbContext.cs`
```csharp
using FashionPipeline.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionPipeline.Core.Data;

public class AppDbContext : DbContext
{
    public Guid CurrentTenantId { get; set; } // Set via ITenantContext

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Accessory> Accessories { get; set; } = null!;
    public DbSet<GeneratedAsset> GeneratedAssets { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Accessory>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.ImageHash).HasMaxLength(64);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId); // SaaS Global Filter
        });
        
        modelBuilder.Entity<GeneratedAsset>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccessoryId, x.PromptUsed }); // Optimization for Cache Lookups
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId); // SaaS Global Filter
        });
    }
}
```

### `FashionPipeline.Core/Options/PromptOptions.cs`
```csharp
using System.Collections.Generic;

namespace FashionPipeline.Core.Options;

public class PromptOptions
{
    public const string SectionName = "Prompts";
    
    // A2A Horizontal Agent Instructions
    public string OrchestratorAgentPrompt { get; set; } = string.Empty;
    public string VisionAgentPrompt { get; set; } = string.Empty;
    public string CreativeAgentPrompt { get; set; } = string.Empty;
    public string MediaAgentPrompt { get; set; } = string.Empty;
    
    // Advanced prompt templates for generating images
    public List<string> ImageGenerationTemplates { get; set; } = new();
}
```

---

## 2. 🔴 REPLACED: MCP Server Projects (Separate Services)

> [!CAUTION]
> The previous approach of placing all tools in `FashionPipeline.Core/Tools` and sharing a single Kernel is **removed**. Delete or empty those files. Each tool now lives in its own dedicated MCP server project.

First, scaffold all 4 MCP server projects from the `src/` directory:

```bash
# Create 4 separate MCP Server projects
dotnet new web -n FashionPipeline.VisionMcpServer -f net8.0
dotnet new web -n FashionPipeline.PromptMcpServer -f net8.0
dotnet new web -n FashionPipeline.ImageMcpServer -f net8.0
dotnet new web -n FashionPipeline.VideoMcpServer -f net8.0

# Add to solution
dotnet sln FashionPipeline.sln add McpServers/FashionPipeline.VisionMcpServer
dotnet sln FashionPipeline.sln add McpServers/FashionPipeline.PromptMcpServer
dotnet sln FashionPipeline.sln add McpServers/FashionPipeline.ImageMcpServer
dotnet sln FashionPipeline.sln add McpServers/FashionPipeline.VideoMcpServer

# Add DotnetFastMCP to each
dotnet add McpServers/FashionPipeline.VisionMcpServer package DotnetFastMCP
dotnet add McpServers/FashionPipeline.PromptMcpServer package DotnetFastMCP
dotnet add McpServers/FashionPipeline.ImageMcpServer package DotnetFastMCP
dotnet add McpServers/FashionPipeline.VideoMcpServer package DotnetFastMCP

# Add FashionPipeline.Core reference (for PromptOptions)
dotnet add McpServers/FashionPipeline.PromptMcpServer reference FashionPipeline.Core
```

### 2A. `VisionMcpServer` — Real Gemini Vision API Call
**Location:** `McpServers/FashionPipeline.VisionMcpServer/`

#### `appsettings.json`
```json
{
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_API_KEY",
    "VisionEndpoint": "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent"
  }
}
```

#### `FeatureExtractionTool.cs`
```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FastMCP;
using Microsoft.Extensions.Options;

namespace FashionPipeline.VisionMcpServer;

public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string VisionEndpoint { get; set; } = string.Empty;
}

public class FeatureExtractionTool
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public FeatureExtractionTool(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    [McpTool("extract_accessory_features", "Calls Gemini Vision to extract JSON features from an image URL")]
    public async Task<string> ExtractFeaturesAsync(string imageUrl)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = "Analyze this fashion accessory image. Return ONLY a JSON object with keys: Color, Type, Material, Vibe, Style. Example: {\"Color\":\"Red\",\"Type\":\"Lace\",\"Material\":\"Silk\",\"Vibe\":\"Wedding\",\"Style\":\"Traditional\"}" },
                        new { inline_data = new { mime_type = "image/jpeg", data = await FetchImageAsBase64Async(imageUrl) } }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_options.VisionEndpoint}?key={_options.ApiKey}", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        // Extract the text from the Gemini response
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "{}";
    }

    private async Task<string> FetchImageAsBase64Async(string imageUrl)
    {
        var bytes = await _httpClient.GetByteArrayAsync(imageUrl);
        return Convert.ToBase64String(bytes);
    }
}
```

### 2B. `PromptMcpServer` — Config-Driven Template Injection (Logic Unchanged)
**Location:** `McpServers/FashionPipeline.PromptMcpServer/`

> [!NOTE]
> The core logic of `PromptGenerationTool` is **unchanged** — template injection via `IOptions`. It just now runs as its own dedicated server.

#### `PromptGenerationTool.cs`
```csharp
using System.Text.Json;
using FastMCP;
using FashionPipeline.Core.Options;
using Microsoft.Extensions.Options;

namespace FashionPipeline.PromptMcpServer;

public class PromptGenerationTool
{
    private readonly PromptOptions _promptOptions;

    public PromptGenerationTool(IOptions<PromptOptions> options)
    {
        _promptOptions = options.Value;
    }

    [McpTool("generate_image_prompts", "Injects accessory features into cinematic prompt templates")]
    public IEnumerable<string> GeneratePrompts(string featureJson)
    {
        var features = JsonSerializer.Deserialize<Dictionary<string, string>>(featureJson)
                       ?? new Dictionary<string, string>();

        var color = features.GetValueOrDefault("Color", "Unknown Color");
        var type = features.GetValueOrDefault("Type", "Accessory");
        var vibe = features.GetValueOrDefault("Vibe", "Elegant");
        var style = features.GetValueOrDefault("Style", "Modern");

        return _promptOptions.ImageGenerationTemplates.Select(template =>
            template
                .Replace("{color}", color)
                .Replace("{type}", type)
                .Replace("{vibe}", vibe)
                .Replace("{style}", style));
    }
}
```

### 2C. `ImageMcpServer` — Real Imagen 3 API Call
**Location:** `McpServers/FashionPipeline.ImageMcpServer/`

#### `appsettings.json`
```json
{
  "Imagen": {
    "ApiKey": "YOUR_IMAGEN_API_KEY",
    "Endpoint": "https://generativelanguage.googleapis.com/v1beta/models/imagen-3.0-generate-002:predict"
  },
  "Storage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "ContainerName": "fashion-assets"
  }
}
```

#### `ImageGenerationTool.cs`
```csharp
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using FastMCP;
using Microsoft.Extensions.Options;

namespace FashionPipeline.ImageMcpServer;

public class ImagenOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public class StorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
}

public class ImageGenerationTool
{
    private readonly HttpClient _httpClient;
    private readonly ImagenOptions _imagenOptions;
    private readonly StorageOptions _storageOptions;

    public ImageGenerationTool(HttpClient httpClient, IOptions<ImagenOptions> imagenOptions, IOptions<StorageOptions> storageOptions)
    {
        _httpClient = httpClient;
        _imagenOptions = imagenOptions.Value;
        _storageOptions = storageOptions.Value;
    }

    [McpTool("generate_accessory_image", "Generates a high-quality image via Imagen 3 and saves to Azure Blob")]
    public async Task<string> GenerateImageAsync(string prompt, string rawImageUri)
    {
        var requestBody = new
        {
            instances = new[] { new { prompt } },
            parameters = new { sampleCount = 1, aspectRatio = "1:1", outputMimeType = "image/webp" }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_imagenOptions.Endpoint}?key={_imagenOptions.ApiKey}", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var base64Image = doc.RootElement
            .GetProperty("predictions")[0]
            .GetProperty("bytesBase64Encoded")
            .GetString() ?? string.Empty;

        // Upload to Azure Blob Storage
        var imageBytes = Convert.FromBase64String(base64Image);
        var blobName = $"images/{Guid.NewGuid()}.webp";
        var blobClient = new BlobContainerClient(_storageOptions.ConnectionString, _storageOptions.ContainerName);
        await blobClient.CreateIfNotExistsAsync();
        var blob = blobClient.GetBlobClient(blobName);
        await blob.UploadAsync(new BinaryData(imageBytes), overwrite: true);

        return blob.Uri.ToString();
    }
}
```

### 2D. `VideoMcpServer` — Real Kling AI API Call with Async Polling
**Location:** `McpServers/FashionPipeline.VideoMcpServer/`

#### `appsettings.json`
```json
{
  "Kling": {
    "ApiKey": "YOUR_KLING_API_KEY",
    "Endpoint": "https://api.klingai.com/v1/videos/image2video"
  },
  "Storage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "ContainerName": "fashion-assets"
  }
}
```

#### `VideoGenerationTool.cs`
```csharp
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using FastMCP;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FashionPipeline.VideoMcpServer;

public class KlingOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public class VideoGenerationTool
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly KlingOptions _klingOptions;
    private readonly StorageOptions _storageOptions;

    public VideoGenerationTool(HttpClient httpClient, IMemoryCache cache,
        IOptions<KlingOptions> klingOptions, IOptions<StorageOptions> storageOptions)
    {
        _httpClient = httpClient;
        _cache = cache;
        _klingOptions = klingOptions.Value;
        _storageOptions = storageOptions.Value;
    }

    [McpTool("generate_accessory_video", "Generates a 5-second promotional video from an image URL via Kling AI")]
    public async Task<string> GenerateVideoAsync(string imageUrl)
    {
        if (_cache.TryGetValue("VideoApi:QuotaExhausted", out bool exhausted) && exhausted)
            return "QUOTA_EXHAUSTED";

        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _klingOptions.ApiKey);

        // 1. Kick off the async video generation job
        var requestBody = new
        {
            model_name = "kling-v1",
            image = imageUrl,
            prompt = "A smooth, cinematic 5-second promotional video of this fashion accessory, elegant slow rotation, studio lighting.",
            duration = "5",
            aspect_ratio = "9:16"
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_klingOptions.Endpoint, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var taskId = doc.RootElement.GetProperty("data").GetProperty("task_id").GetString()!;

        // 2. Poll for completion (Kling AI is asynchronous)
        string? videoUrl = null;
        for (int i = 0; i < 30; i++) // Poll for up to 5 minutes
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            var statusResponse = await _httpClient.GetAsync($"{_klingOptions.Endpoint}/{taskId}");
            var statusJson = await statusResponse.Content.ReadAsStringAsync();
            using var statusDoc = JsonDocument.Parse(statusJson);
            var status = statusDoc.RootElement.GetProperty("data").GetProperty("task_status").GetString();
            if (status == "succeed")
            {
                videoUrl = statusDoc.RootElement.GetProperty("data").GetProperty("task_result")
                    .GetProperty("videos")[0].GetProperty("url").GetString();
                break;
            }
            if (status == "failed") throw new Exception("Kling video generation failed.");
        }

        if (videoUrl == null) throw new TimeoutException("Kling video generation timed out.");

        // 3. Download and save to Azure Blob Storage
        var videoBytes = await _httpClient.GetByteArrayAsync(videoUrl);
        var blobName = $"videos/{Guid.NewGuid()}.mp4";
        var blobClient = new BlobContainerClient(_storageOptions.ConnectionString, _storageOptions.ContainerName);
        await blobClient.CreateIfNotExistsAsync();
        var blob = blobClient.GetBlobClient(blobName);
        await blob.UploadAsync(new BinaryData(videoBytes), overwrite: true);

        return blob.Uri.ToString();
    }
}

---

## 3. 🔴 REPLACED: Orchestration (A2A HTTP Client)

> [!CAUTION]
> The previous `PipelineAgentJob` used `AgentGroupChat` (in-process). This is **replaced** by a true A2A client that calls the Orchestrator Agent over HTTP using the `A2A` NuGet package.

### Install A2A SDK

```bash
dotnet add FashionPipeline.Core package A2A
```

### `PipelineAgentJob.cs` — A2A Client

```csharp
using A2A;
using A2A.Models;
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;
using FashionPipeline.Core.Options;
using Microsoft.Extensions.Options;

namespace FashionPipeline.Core.Jobs;

public class AgentOptions
{
    public string OrchestratorUrl { get; set; } = string.Empty;
}

public class PipelineAgentJob
{
    private readonly AppDbContext _dbContext;
    private readonly AgentOptions _agentOptions;
    private readonly HttpClient _httpClient;

    public PipelineAgentJob(AppDbContext dbContext, IOptions<AgentOptions> agentOptions, HttpClient httpClient)
    {
        _dbContext = dbContext;
        _agentOptions = agentOptions.Value;
        _httpClient = httpClient;
    }

    public async Task ExecuteAsync(Guid accessoryId, Guid tenantId)
    {
        _dbContext.CurrentTenantId = tenantId;
        var accessory = await _dbContext.Accessories.FindAsync(accessoryId);
        if (accessory == null) return;

        try
        {
            accessory.Status = AccessoryStatus.Processing;
            await _dbContext.SaveChangesAsync();

            // 1. Create an A2A client pointing to the Orchestrator Agent HTTP service
            var a2aClient = new A2AClient(_httpClient, new Uri(_agentOptions.OrchestratorUrl));

            // 2. Send the task to the Orchestrator via A2A task/send (JSON-RPC 2.0)
            var taskPayload = new A2ATask
            {
                Message = new Message
                {
                    Role = "user",
                    Parts = new[]
                    {
                        new TextPart
                        {
                            Text = $"Process accessory. ID: {accessory.Id}. TenantId: {tenantId}. ImageUrl: {accessory.RawImageUri}"
                        }
                    }
                }
            };

            // 3. Send and stream the response from the Orchestrator Agent
            await foreach (var streamEvent in a2aClient.SendTaskStreamingAsync(taskPayload))
            {
                // Log progress from A2A streaming response
                Console.WriteLine($"[A2A Event]: {streamEvent?.Result?.Status?.Message?.Parts?.FirstOrDefault()?.ToString()}");
            }

            // 4. Check final status from DB (Orchestrator Agent updates DB directly)
            await _dbContext.Entry(accessory).ReloadAsync();
            if (accessory.Status != AccessoryStatus.Complete && accessory.Status != AccessoryStatus.RequiresManualVideo)
            {
                accessory.Status = AccessoryStatus.Complete;
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception)
        {
            accessory.Status = AccessoryStatus.Failed;
            await _dbContext.SaveChangesAsync();
            throw; // Let Hangfire retry
        }
    }
}
```



### `PipelineAgentJob.cs`
```csharp
using System;
using System.Threading.Tasks;
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;
using FashionPipeline.Core.Options;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;

namespace FashionPipeline.Core.Jobs;

public class PipelineAgentJob
{
    private readonly Kernel _kernel;
    private readonly AppDbContext _dbContext;
    private readonly PromptOptions _promptOptions;

    public PipelineAgentJob(Kernel kernel, AppDbContext dbContext, IOptions<PromptOptions> promptOptions)
    {
        _kernel = kernel;
        _dbContext = dbContext;
        _promptOptions = promptOptions.Value;
    }

    public async Task ExecuteAsync(Guid accessoryId, Guid tenantId)
    {
        _dbContext.CurrentTenantId = tenantId;
        var accessory = await _dbContext.Accessories.FindAsync(accessoryId);
        if (accessory == null) return;

        try
        {
            accessory.Status = AccessoryStatus.Processing;
            await _dbContext.SaveChangesAsync();

            // 1. Define Specialized Agents (A2A Horizontal Layer)
            var orchestratorAgent = new ChatCompletionAgent
            {
                Name = "OrchestratorAgent",
                Instructions = _promptOptions.OrchestratorAgentPrompt,
                Kernel = _kernel
            };

            var visionAgent = new ChatCompletionAgent
            {
                Name = "VisionAgent",
                Instructions = _promptOptions.VisionAgentPrompt,
                Kernel = _kernel // Has FeatureExtractionTool
            };

            var creativeAgent = new ChatCompletionAgent
            {
                Name = "CreativeAgent",
                Instructions = _promptOptions.CreativeAgentPrompt,
                Kernel = _kernel // Has PromptGenerationTool
            };

            var mediaAgent = new ChatCompletionAgent
            {
                Name = "MediaAgent",
                Instructions = _promptOptions.MediaAgentPrompt,
                Kernel = _kernel // Has Image/Video Tools
            };

            // 2. Create the Agent Group Chat (The Horizontal Orchestrator)
            // Note: Requires 'Microsoft.SemanticKernel.Agents' NuGet package
            var chat = new AgentGroupChat(orchestratorAgent, visionAgent, creativeAgent, mediaAgent);
            chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, $"Process Accessory ID: {accessory.Id}. Image URL: {accessory.RawImageUri}"));

            // 3. Execution Loop
            await foreach (var response in chat.InvokeAsync())
            {
                // Here you can log inter-agent A2A communication
                Console.WriteLine($"[{response.AuthorName}]: {response.Content}");
            }

            accessory.Status = AccessoryStatus.Complete;
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception)
        {
            accessory.Status = AccessoryStatus.Failed;
            await _dbContext.SaveChangesAsync();
            throw; // Let Hangfire retry
        }
    }
}
```

---

## 4. API & Configuration (The Missing Pieces)

These are the essential API configuration files required to bootstrap the database, Hangfire, Semantic Kernel, and the Ingestion endpoint. Create them in `FashionPipeline.Api`.

### `appsettings.json`
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=fashion_pipeline.db" // Replaced via env vars in Prod (PostgreSQL)
  },
  "Storage": {
    "Provider": "AzureBlob",
    "ConnectionString": "UseDevelopmentStorage=true",
    "ContainerName": "fashion-assets"
  },
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_API_KEY_HERE"
  },
  "Prompts": {
    "OrchestratorAgentPrompt": "You are the Orchestrator. Coordinate with the VisionAgent, CreativeAgent, and MediaAgent to process the accessory.",
    "VisionAgentPrompt": "You are the Vision Agent. Use your MCP tools to extract features from images.",
    "CreativeAgentPrompt": "You are the Creative Agent. Use your MCP tools to convert JSON features into image prompts.",
    "MediaAgentPrompt": "You are the Media Agent. Use your MCP tools to generate images and videos.",
    "ImageGenerationTemplates": [
      "A high-fashion studio shot of a {color} {type} draped over an elegant mannequin. Cinematic lighting, photorealistic, 8k resolution, highly detailed.",
      "A close-up macro shot of a {color} {type} showing intricate stitching and texture details, soft diffuse lighting, shallow depth of field.",
      "A cinematic shot of a {color} {type} placed gracefully on a dark velvet background. Moody lighting, luxury fashion editorial style."
    ]
  }
}
```

### `Program.cs`
```csharp
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;
using FashionPipeline.Core.Jobs;
using FashionPipeline.Core.Options;
using FashionPipeline.Core.Tools;
using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Options
builder.Services.Configure<PromptOptions>(builder.Configuration.GetSection(PromptOptions.SectionName));

// 2. Configure Database (Environment Aware)
// In Production, this should read "Npgsql" or "SqlServer" from config
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. Configure Hangfire (Environment Aware)
builder.Services.AddHangfire(config => config
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHangfireServer();

// 4. Configure Semantic Kernel & Tools
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<FeatureExtractionTool>();
builder.Services.AddTransient<PromptGenerationTool>();
builder.Services.AddTransient<ImageGenerationTool>();
builder.Services.AddTransient<VideoGenerationTool>();

// Initialize Kernel with Gemini
var kernelBuilder = Kernel.CreateBuilder();
// NOTE: Use Semantic Kernel's Gemini connector package when building for real
// kernelBuilder.AddGeminiChatCompletion("gemini-2.5-flash", builder.Configuration["Gemini:ApiKey"]);
builder.Services.AddTransient<Kernel>(sp =>
{
    var kernel = kernelBuilder.Build();
    // In FastMCP, tools are injected as plugins here
    return kernel;
});

var app = builder.Build();

// 5. Minimal API Endpoint: Ingestion (Tenant Aware)
app.MapPost("/api/v1/accessory/process", async (string name, string category, string imageUrl, Guid tenantId, AppDbContext db, IBackgroundJobClient jobClient) =>
{
    // Set Tenant Context for this request
    db.CurrentTenantId = tenantId;

    var accessory = new Accessory
    {
        TenantId = tenantId,
        Name = name,
        Category = category,
        RawImageUri = imageUrl
    };

    db.Accessories.Add(accessory);
    await db.SaveChangesAsync();

    // Pass TenantId to background job so Hangfire executes in correct context
    var jobId = jobClient.Enqueue<PipelineAgentJob>(job => job.ExecuteAsync(accessory.Id, tenantId));

    return Results.Accepted($"/api/v1/accessory/{accessory.Id}", new { JobId = jobId, AccessoryId = accessory.Id });
});

app.UseHangfireDashboard();
app.Run();
```

---

## 5. Comprehensive Testing Strategy (Production Grade)

For an enterprise-grade application, we target >95% unit test coverage, full integration tests, and Playwright E2E tests.

### 5.1 Create Test Projects & Add Dependencies
To set up the testing projects in VS Code, open the integrated terminal (`Ctrl + ~`) and execute the following commands from the `src` directory:

```bash
# 1. Create Unit & Integration Test Project
dotnet new xunit -n FashionPipeline.Tests -f net8.0
dotnet sln FashionPipeline.sln add FashionPipeline.Tests/FashionPipeline.Tests.csproj
dotnet add FashionPipeline.Tests/FashionPipeline.Tests.csproj reference FashionPipeline.Core/FashionPipeline.Core.csproj

# Add required NuGet packages
dotnet add FashionPipeline.Tests/FashionPipeline.Tests.csproj package Verify.Xunit
dotnet add FashionPipeline.Tests/FashionPipeline.Tests.csproj package Moq
dotnet add FashionPipeline.Tests/FashionPipeline.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory

# 2. Create Playwright E2E Test Project
dotnet new nunit -n FashionPipeline.E2ETests -f net8.0
dotnet sln FashionPipeline.sln add FashionPipeline.E2ETests/FashionPipeline.E2ETests.csproj
dotnet add FashionPipeline.E2ETests/FashionPipeline.E2ETests.csproj package Microsoft.Playwright.NUnit
# Install Playwright browsers (run this after building)
# pwsh bin/Debug/net8.0/playwright.ps1 install
```

### 5.2 Layer 1: Comprehensive Unit Tests (>95% Coverage)
To achieve >95% coverage, we test all tools by mocking their external dependencies (like `HttpClient` or `IMemoryCache`).

#### `FeatureExtractionToolTests.cs`
```csharp
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FashionPipeline.Core.Tools;
using Moq;
using Moq.Protected;
using Xunit;

namespace FashionPipeline.Tests.Tools;

public class FeatureExtractionToolTests
{
    [Fact]
    public async Task ExtractFeaturesAsync_ReturnsExpectedJson()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{\"Color\":\"Red\"}") });
        
        var httpClient = new HttpClient(handlerMock.Object);
        var tool = new FeatureExtractionTool(httpClient);

        // Act
        var result = await tool.ExtractFeaturesAsync("http://image.com/test.jpg");

        // Assert
        Assert.Contains("Red", result);
    }
}
```

#### `VideoGenerationToolTests.cs`
```csharp
using System.Threading.Tasks;
using FashionPipeline.Core.Tools;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FashionPipeline.Tests.Tools;

public class VideoGenerationToolTests
{
    [Fact]
    public async Task GenerateVideoAsync_ReturnsQuotaExhausted_WhenCacheSet()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMemoryCache();
        var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IMemoryCache>();
        cache.Set("VideoApi:QuotaExhausted", true);

        var tool = new VideoGenerationTool(cache);

        // Act
        var result = await tool.GenerateVideoAsync("http://image.com/test.jpg");

        // Assert
        Assert.Equal("API Quota exhausted. Notify user to perform manual video generation step.", result);
    }
}
```

### 5.3 Layer 2: Snapshot Tests (`PromptGenerationToolTests.cs`)
We use `Verify.Xunit` to lock prompt generation logic. If the prompt template changes, the test fails until explicitly approved.

```csharp
using System.Threading.Tasks;
using FashionPipeline.Core.Tools;
using VerifyXunit;
using Xunit;

namespace FashionPipeline.Tests.Tools;

[UsesVerify]
public class PromptGenerationToolTests
{
    [Fact]
    public async Task Generates_Expected_Prompts_For_Valid_Features()
    {
        // Arrange
        var mockOptions = Options.Create(new PromptOptions 
        {
            ImageGenerationTemplates = new List<string> { "A {color} {type} shot." }
        });
        
        var tool = new PromptGenerationTool(mockOptions);
        var featureJson = "{\"Color\": \"Royal Blue\", \"Type\": \"Latkan\"}";

        // Act
        var prompts = tool.GeneratePrompts(featureJson);

        // Assert - Snapshot Test
        await Verifier.Verify(prompts);
    }
}
```

### 5.4 Layer 3: Integration Tests (`PipelineIntegrationTests.cs`)
We test the Hangfire Job end-to-end using an in-memory SQLite database and a mocked Semantic Kernel.

```csharp
using System;
using System.Threading.Tasks;
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;
using FashionPipeline.Core.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Xunit;

namespace FashionPipeline.Tests.Integration;

public class PipelineIntegrationTests
{
    [Fact]
    public async Task Job_Changes_Status_To_Complete_On_Success()
    {
        // Arrange: In-Memory DB
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new AppDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var accessory = new Accessory { Id = Guid.NewGuid(), RawImageUri = "https://mock.com/img.jpg" };
        dbContext.Accessories.Add(accessory);
        await dbContext.SaveChangesAsync();

        // Arrange: Mock Kernel
        var builder = Kernel.CreateBuilder();
        // NOTE: In real tests, inject a Mock IChatCompletionService here to avoid hitting Gemini API
        var kernel = builder.Build(); 

        var job = new PipelineAgentJob(kernel, dbContext);

        // Act
        await job.ExecuteAsync(accessory.Id);

        // Assert
        var updatedAccessory = await dbContext.Accessories.FindAsync(accessory.Id);
        Assert.Equal(AccessoryStatus.Complete, updatedAccessory!.Status);
    }
}
```

```

### 5.5 Layer 4: Playwright E2E Tests
This test verifies the Swagger UI and API endpoints end-to-end. Create this in the `FashionPipeline.E2ETests` project.

#### `SwaggerE2ETests.cs`
```csharp
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Threading.Tasks;

namespace FashionPipeline.E2ETests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class SwaggerE2ETests : PageTest
{
    [Test]
    public async Task Can_Submit_Accessory_Via_Swagger()
    {
        // Navigate to the Swagger UI of the running API (assuming port 5000)
        await Page.GotoAsync("http://localhost:5000/swagger");

        // Assert title
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Swagger UI"));

        // Open the POST endpoint
        await Page.Locator("div[id^='operations-default-post_api_v1_accessory_process']").ClickAsync();

        // Click Try it out
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Try it out" }).ClickAsync();

        // Fill parameters
        await Page.Locator("input[placeholder='name']").FillAsync("Gold Lace");
        await Page.Locator("input[placeholder='category']").FillAsync("Lace");
        await Page.Locator("input[placeholder='imageUrl']").FillAsync("https://example.com/lace.jpg");
        await Page.Locator("input[placeholder='tenantId']").FillAsync("12345678-1234-1234-1234-123456789012");

        // Execute
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Execute" }).ClickAsync();

        // Verify response is 202 Accepted
        var responseCode = Page.Locator(".response-col_status").First;
        await Expect(responseCode).ToContainTextAsync("202");
    }
}
```

---

## 6. Developer Documentation
Please create the following documentation file.

### `docs/developer_guide.md`
```markdown
# Fashion Pipeline - Developer Guide

## Prerequisites
- .NET 8 SDK
- Docker (for local testing of infrastructure)

## Running Locally
1. Run `dotnet ef database update` to create the SQLite database.
2. Start the API using `dotnet run --project src/FashionPipeline.Api`.
3. The Hangfire dashboard is available at `http://localhost:5000/hangfire`.
4. Swagger UI is available at `http://localhost:5000/swagger`.

## Step-by-Step Manual Testing Guide
1. **Start the Application:** Ensure the API is running via `dotnet run`.
2. **Access Swagger:** Open a browser and navigate to `http://localhost:5000/swagger`.
3. **Submit a Request:** 
   - Expand the `POST /api/v1/accessory/process` endpoint.
   - Click **Try it out**.
   - Enter a test `name` (e.g., "Red Latkan").
   - Enter a `category` (e.g., "Latkan").
   - Enter an `imageUrl` (e.g., "https://example.com/red-latkan.jpg").
   - Enter a valid GUID for `tenantId` (e.g., `11111111-1111-1111-1111-111111111111`).
   - Click **Execute**.
   - **Verify:** You should receive a `202 Accepted` response containing a `JobId`.
4. **Monitor Background Job:**
   - Open a new tab and navigate to `http://localhost:5000/hangfire`.
   - Go to the **Jobs -> Processing** tab. You should see `PipelineAgentJob.ExecuteAsync` running.
   - Once complete, it moves to the **Succeeded** tab.
5. **Verify Database:**
   - Use a SQLite browser (like DB Browser for SQLite) to open `fashion_pipeline.db`.
   - Check the `Accessories` table. The status should be `Complete`.

## Testing Suite Execution
- **Unit & Integration Tests:** `dotnet test src/FashionPipeline.Tests` (Runs < 5 seconds, targets >95% coverage).
- **Snapshot Tests:** If prompt tests fail due to deliberate template changes, run `dotnet verify accept`.
- **E2E Playwright Tests:** Ensure API is running, then execute `dotnet test src/FashionPipeline.E2ETests`.
```
