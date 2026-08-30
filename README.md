# 👗 Fashion Accessory AI Marketing Pipeline

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0%20LTS-blue.svg)](https://dotnet.microsoft.com/)
[![C# 13](https://img.shields.io/badge/C%23-13-purple.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![DotnetFastMCP](https://img.shields.io/badge/MCP-DotnetFastMCP%20v2.0-orange.svg)](https://github.com/tekspry/DotnetFastMCP)
[![Google A2A](https://img.shields.io/badge/Protocol-Google%20A2A-green.svg)](https://a2a-protocol.org)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

An enterprise-grade, distributed multimodal AI pipeline built in **.NET 10 LTS** that automates the transformation of raw physical fashion accessory photographs (buttons, zari laces, trims, latkans, and brooches) into commercial 2-section marketing visuals and video content.

The system pioneers a **Two-Dimensional AI Architecture**:
- 🌐 **Horizontal Layer (Google A2A Protocol):** Autonomous micro-agents communicating via JSON-RPC 2.0 (`task/send`) with discoverable AgentCards.
- 🏗️ **Vertical Layer (DotnetFastMCP):** Standalone Model Context Protocol (MCP) servers with first-class ASP.NET Core Dependency Injection.

---

## 🌟 Key Features & Breakthroughs

- 🎨 **Multimodal Dual-Conditioning Image Synthesis:** Ingests the raw physical accessory photo (as base64 `inlineData`) alongside structured layout prompts into **`gemini-3.1-flash-image`**, faithfully replicating micro-textures, facets, and metallic lusters in ~12–14 seconds.
- 📐 **2-Section Horizontal Composite Layout (9:16 Portrait):**
  - **Top 20% Section:** Macro close-up of the exact accessory on a luxury stone surface with white typography banner (`COLOR | TYPE | DIMENSION`).
  - **Bottom 80% Section:** Full-body portrait of an executive model wearing an Indian designer suit with the exact accessory applied down plackets, cuffs, or borders.
- 🔄 **Content-Addressable SHA-256 Vision Caching:** Prevents redundant vision API calls when processing identical accessories across multiple marketing campaigns.
- 🛡️ **Multi-Tenant SaaS Isolation:** Native `TenantId` isolation enforced across all database queries and background jobs via Entity Framework Core Global Query Filters.
- ⚙️ **Multi-Provider Agility:** Primary generation via Google AI Studio (`gemini-3.6-flash` / `gemini-3.1-flash-image`) with seamless configuration-based fallback to Azure AI Foundry (`gpt-5.6-sol` / `FLUX.1-Kontext-pro`).
- ⏱️ **Decoupled Asynchronous Processing:** Background workflow execution powered by **Hangfire** with built-in exponential backoff rate-limit protection.

---

## 🏛️ System Architecture

```
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│                                 CONTROL & INGESTION LAYER                                 │
│  FashionPipeline.Api (:5000)  ──►  Hangfire Queue (:5000/hangfire)                        │
└─────────────────────────────────────────────┬─────────────────────────────────────────────┘
                                              │ A2A task/send (JSON-RPC 2.0)
                                              ▼
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│                             HORIZONTAL AGENT LAYER (Google A2A)                           │
│  OrchestratorAgent (:5050) ──► VisionAgent (:5101) ──► CreativeAgent (:5201)             │
│                            ──► ImageAgent (:5301)  ──► InpaintingAgent (:5501)            │
│                            ──► VideoAgent (:5401)                                         │
└─────────────────────────────────────────────┬─────────────────────────────────────────────┘
                                              │ MCP Tool Invocations
                                              ▼
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│                           VERTICAL MCP TOOL LAYER (DotnetFastMCP)                         │
│  VisionMcpServer (:5100)       PromptMcpServer (:5200)      ImageMcpServer (:5300)        │
│  InpaintingMcpServer (:5500)   VideoMcpServer (:5400)                                     │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

### Port Allocation & Service Map

| Port | Service Name | Protocol | Role / Underlying Tool |
|---|---|---|---|
| `:5000` | **`FashionPipeline.Api`** | REST / Swagger | Ingestion API, SQLite DB, Hangfire dashboard |
| `:5050` | **`OrchestratorAgent`** | A2A JSON-RPC | Master coordinator & step-by-step state persistence |
| `:5101` | **`VisionAgent`** | A2A JSON-RPC | Coordinates feature extraction requests |
| `:5201` | **`CreativeAgent`** | A2A JSON-RPC | Formulates 2-section composite layout prompts |
| `:5301` | **`ImageAgent`** | A2A JSON-RPC | Coordinates multimodal image synthesis |
| `:5501` | **`InpaintingAgent`** | A2A JSON-RPC | Virtual Try-On (VTON) & garment inpainting worker |
| `:5401` | **`VideoAgent`** | A2A JSON-RPC | Video motion synthesis worker |
| `:5100` | **`VisionMcpServer`** | FastMCP | `extract_accessory_features` (`gemini-3.6-flash`) |
| `:5200` | **`PromptMcpServer`** | FastMCP | `generate_image_prompts` (Template injection) |
| `:5300` | **`ImageMcpServer`** | FastMCP | `generate_accessory_image` (`gemini-3.1-flash-image`) |
| `:5500` | **`InpaintingMcpServer`** | FastMCP | `inpaint_accessory` (VTON engine) |
| `:5400` | **`VideoMcpServer`** | FastMCP | `generate_accessory_video` (Kling AI) |

---

## 🚀 Quick Start

### 1. Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PowerShell 7+](https://github.com/PowerShell/PowerShell)
- A [Google AI Studio API Key](https://aistudio.google.com/)

### 2. Clone the Repository
```bash
git clone https://github.com/tekspry/fashion-pipeline.git
cd fashion-pipeline
```

### 3. Configure Your API Key
Add your Google AI Studio key to `appsettings.Local.json` inside the MCP server projects (or set via environment variable `AIProvider__Google__ApiKey`):

```json
{
  "AIProvider": {
    "Provider": "Google",
    "Google": {
      "ApiKey": "YOUR_GOOGLE_AI_STUDIO_API_KEY"
    }
  },
  "Gemini": {
    "ApiKey": "YOUR_GOOGLE_AI_STUDIO_API_KEY"
  }
}
```

### 4. Launch All 10 Microservices
Run the automated background launcher script:

```powershell
pwsh -File src/scripts/start-bg-services.ps1
```

*(To view live console output across separate windows, run `pwsh -File src/scripts/start-phase1.ps1` instead).*

---

## 📡 End-to-End API Usage

### Step 1: Upload a Raw Accessory Photograph
```bash
curl -X POST "http://localhost:5000/api/v1/accessory/upload" \
  -H "X-Tenant-Id: 11111111-1111-1111-1111-111111111111" \
  -F "file=@sample_button.jpg" \
  -F "name=Champagne Gold Starfish Button" \
  -F "category=Button"
```

**Response (`201 Created`):**
```json
{
  "id": "26171827-80f2-4853-b459-cfaf2081de5c",
  "name": "Champagne Gold Starfish Button",
  "category": "Button",
  "status": "Pending",
  "rawImageUri": "http://localhost:5000/uploads/26171827-80f2-4853-b459-cfaf2081de5c_sample_button.jpg"
}
```

### Step 2: Trigger End-to-End Generation
```bash
curl -X POST "http://localhost:5000/api/v1/accessory/26171827-80f2-4853-b459-cfaf2081de5c/run" \
  -H "X-Tenant-Id: 11111111-1111-1111-1111-111111111111"
```

**Response (`202 Accepted`):**
```json
{
  "accessoryId": "26171827-80f2-4853-b459-cfaf2081de5c",
  "status": "Processing",
  "jobId": "42"
}
```

### Step 3: Monitor Execution
- **Swagger UI:** `http://localhost:5000/swagger`
- **Hangfire Queue Dashboard:** `http://localhost:5000/hangfire`

### Step 4: Retrieve Generated Visual Assets
```bash
curl -X GET "http://localhost:5000/api/v1/accessory/26171827-80f2-4853-b459-cfaf2081de5c/assets" \
  -H "X-Tenant-Id: 11111111-1111-1111-1111-111111111111"
```

**Response (`200 OK`):**
```json
[
  {
    "id": "e4b67f18-2c70-4d51-8742-fa32c8427901",
    "accessoryId": "26171827-80f2-4853-b459-cfaf2081de5c",
    "assetType": "Image",
    "assetUri": "file:///c:/pocs/FastMCP/FashionAccessoryPipeline/src/McpServers/FashionPipeline.ImageMcpServer/output/26171827-80f2-4853-b459-cfaf2081de5c.webp",
    "promptUsed": "High-fashion two-part horizontal composite marketing visual...",
    "isApproved": true,
    "createdAt": "2026-08-27T02:10:00Z"
  }
]
```

---

## 💻 Building Tools with DotnetFastMCP

Each MCP server exposes declarative `[McpTool]` methods with first-class ASP.NET Core Dependency Injection:

```csharp
// VisionMcpServer/FeatureExtractionTool.cs
using FastMCP.Attributes;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using FashionPipeline.Core.Options;

namespace FashionPipeline.VisionMcpServer;

public class FeatureExtractionTool
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _providerOptions;
    private readonly ILogger<FeatureExtractionTool> _logger;

    // Resolved automatically from ASP.NET Core IServiceProvider
    public FeatureExtractionTool(
        HttpClient httpClient, 
        IOptions<AiProviderOptions> providerOptions, 
        ILogger<FeatureExtractionTool> logger)
    {
        _httpClient = httpClient;
        _providerOptions = providerOptions.Value;
        _logger = logger;
    }

    [McpTool("extract_accessory_features", 
             Description = "Extracts structured design, color, and dimension metadata from an accessory image URL.")]
    public async Task<string> ExtractFeaturesAsync(string imageUrl)
    {
        _logger.LogInformation("Analyzing accessory features for {Url}", imageUrl);
        return await ExtractFeaturesViaGoogleAsync(imageUrl);
    }
}
```

---

## 📂 Project Structure

```
fashion-pipeline/
├── src/
│   ├── FashionPipeline.Api/                     # Ingestion Minimal API & SQLite storage
│   ├── FashionPipeline.Core/                    # Shared entities, AppDbContext, tenant options
│   │
│   ├── Agents/                                  # Horizontal A2A Agents
│   │   ├── FashionPipeline.OrchestratorAgent/   # Workflow coordinator & step persistence (:5050)
│   │   ├── FashionPipeline.VisionAgent/         # Vision analysis coordinator (:5101)
│   │   ├── FashionPipeline.CreativeAgent/       # Prompt formulation coordinator (:5201)
│   │   ├── FashionPipeline.ImageAgent/          # Image generation coordinator (:5301)
│   │   ├── FashionPipeline.InpaintingAgent/     # Virtual Try-On (VTON) specialist (:5501)
│   │   └── FashionPipeline.VideoAgent/          # Video animation specialist (:5401)
│   │
│   ├── McpServers/                              # Vertical DotnetFastMCP Tool Servers
│   │   ├── FashionPipeline.VisionMcpServer/     # Gemini 3.6 Flash feature extraction (:5100)
│   │   ├── FashionPipeline.PromptMcpServer/     # 2-section composite prompt templates (:5200)
│   │   ├── FashionPipeline.ImageMcpServer/      # Gemini 3.1 Flash Image multimodal gen (:5300)
│   │   ├── FashionPipeline.InpaintingMcpServer/ # VTON / Inpainting server (:5500)
│   │   └── FashionPipeline.VideoMcpServer/      # Kling AI video generation server (:5400)
│   │
│   └── scripts/
│       ├── start-bg-services.ps1                # Background process launcher
│       └── start-phase1.ps1                     # Interactive multi-window launcher
│
├── tests/
│   ├── FashionPipeline.Tests/                   # xUnit unit & integration tests
│   └── FashionPipeline.E2ETests/                # Playwright end-to-end tests
│
├── LICENSE                                      # MIT Open Source License
└── README.md                                    # Project documentation
```

---

## 🧪 Testing

```bash
# Run unit & integration test suite
dotnet test src/FashionPipeline.Tests/FashionPipeline.Tests.csproj

# Run Playwright E2E tests
dotnet test src/FashionPipeline.E2ETests/FashionPipeline.E2ETests.csproj
```

---

## 🤝 Related Projects & Ecosystem

- **[DotnetFastMCP](https://github.com/tekspry/DotnetFastMCP)** — The lightweight, high-performance Model Context Protocol framework for .NET.
- **[Google Agent-to-Agent (A2A) Protocol](https://a2a-protocol.org)** — Open standard for inter-agent communication.
- **[Model Context Protocol (MCP)](https://modelcontextprotocol.io)** — Open standard for connecting AI models to data and tools.

---

## 📜 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.
