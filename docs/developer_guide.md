# Fashion Pipeline - Developer Guide

## Prerequisites
- .NET 8 SDK
- Docker (for local testing of infrastructure)
- DB Browser for SQLite — download from https://sqlitebrowser.org/ (for inspecting the database)

---

## Running Locally

1. Apply database migrations to create the SQLite database:
   ```bash
   dotnet ef database update --project src/FashionPipeline.Core --startup-project src/FashionPipeline.Api
   ```
2. Start the API:
   ```bash
   dotnet run --project src/FashionPipeline.Api
   ```
3. The following URLs will be available:
   - **Swagger UI:** `http://localhost:5000/swagger`
   - **Hangfire Dashboard:** `http://localhost:5000/hangfire`

---

## Manual Testing Guide: Verify Image & Video Generation

Follow these steps to manually trigger the pipeline and confirm that images and videos are being generated correctly.

### Step 1 — Trigger the Pipeline via Swagger

1. Open your browser and navigate to `http://localhost:5000/swagger`.
2. Find and expand the `POST /api/v1/accessory/process` endpoint.
3. Click **Try it out**.
4. Fill in the parameters:
   | Parameter  | Example Value |
   |---|---|
   | `name`     | `Red Silk Latkan` |
   | `category` | `Latkan` |
   | `imageUrl` | `https://upload.wikimedia.org/wikipedia/commons/thumb/4/47/PNG_transparency_demonstration_1.png/280px-PNG_transparency_demonstration_1.png` |
   | `tenantId` | `11111111-1111-1111-1111-111111111111` |
5. Click **Execute**.
6. **Expected result:** A `202 Accepted` response containing a `JobId` and `AccessoryId`.
   ```json
   {
     "jobId": "1",
     "accessoryId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
   }
   ```

### Step 2 — Monitor the Background Job (Hangfire Dashboard)

1. Open `http://localhost:5000/hangfire`.
2. Click on **Jobs** in the top navigation.
3. Watch the job lifecycle:
   - **Enqueued** → The job is waiting to be picked up.
   - **Processing** → The A2A agents are running. This is where Gemini, Imagen, and Kling APIs are being called.
   - **Succeeded** → The pipeline completed without errors.
   - **Failed** → If it fails, click the job to see the full exception and stack trace.

### Step 3 — Verify Generated Images & Videos in the Database

1. Open `fashion_pipeline.db` in **DB Browser for SQLite**.
2. Run the following SQL query to see the generated assets and the exact prompts used:

   ```sql
   -- Check all generated images and videos for the accessory
   SELECT
       a.Name,
       a.Status,
       ga.AssetType,
       ga.AssetUri,
       ga.PromptUsed,
       ga.IsApproved,
       ga.CreatedAt
   FROM GeneratedAssets ga
   JOIN Accessories a ON ga.AccessoryId = a.Id
   ORDER BY ga.CreatedAt DESC;
   ```

3. **What to verify:**
   - `AssetType` should contain rows for both `Image` and `Video`.
   - `AssetUri` should be a valid storage URL for each generated file.
   - `PromptUsed` should show the fully-expanded, cinematic prompt that was sent to Imagen 3.
   - The parent `Accessories.Status` should read `Complete`.

---

## Understanding the Prompts at Each Pipeline Stage

The pipeline uses the **A2A (Agent-to-Agent)** model. Each agent has its own specialized prompt. All prompts are externalized in `src/FashionPipeline.Api/appsettings.json` under the `Prompts` section — **no code change required to update them.**

| Stage | Agent | Config Key | What it does |
|---|---|---|---|
| **1. Coordination** | OrchestratorAgent | `OrchestratorAgentPrompt` | Manages workflow; delegates to other agents |
| **2. Image Analysis** | VisionAgent | `VisionAgentPrompt` | Calls `extract_accessory_features` MCP tool on Gemini Vision |
| **3. Creative Writing** | CreativeAgent | `CreativeAgentPrompt` + `ImageGenerationTemplates` | Injects extracted features into the cinematic templates |
| **4. Media Generation** | MediaAgent | `MediaAgentPrompt` | Calls `generate_accessory_image` and `generate_accessory_video` MCP tools |

### Inspecting the Dynamic Image Generation Prompts

The final prompts sent to Imagen 3 are built dynamically by the **Creative Agent**. The templates are in `appsettings.json`:

```json
"ImageGenerationTemplates": [
  "A high-fashion studio shot of a {color} {type} draped over an elegant mannequin. Cinematic lighting, photorealistic, 8k resolution.",
  "A close-up macro shot of a {color} {type} showing intricate stitching and texture details, shallow depth of field.",
  "A cinematic shot of a {color} {type} on a dark velvet background. Moody lighting, luxury fashion editorial style."
]
```

For an accessory with `Color: Red` and `Type: Latkan`, the generated prompts become:
- `"A high-fashion studio shot of a Red Latkan draped over an elegant mannequin..."`
- `"A close-up macro shot of a Red Latkan showing intricate stitching..."`
- `"A cinematic shot of a Red Latkan on a dark velvet background..."`

> **Tip:** To change or improve the prompts, edit `appsettings.json` and restart the API. No code change or redeployment needed.

---

## Console Logging for Agent Communication

While a job is running, the inter-agent A2A conversation is printed to the VS Code terminal. Watch for output like:

```
[OrchestratorAgent]: Starting pipeline for Accessory abc-123...
[VisionAgent]: Extracted features: {"Color": "Red", "Type": "Latkan", "Vibe": "Wedding"}
[CreativeAgent]: Generated 3 cinematic prompts.
[MediaAgent]: Generated image 1 of 3. URL: https://storage.example.com/...
[MediaAgent]: Video generation complete. URL: https://storage.example.com/video.mp4
```

---

## Testing Suite Execution

| Test Type | Command | Speed |
|---|---|---|
| Unit & Integration | `dotnet test src/FashionPipeline.Tests` | < 5 seconds |
| Snapshot approval | `dotnet verify accept` | Instant |
| E2E Playwright | `dotnet test src/FashionPipeline.E2ETests` | ~30 seconds |

---

## Architecture
See `ARCHITECTURE_PHASE1.md` for a complete system overview and sequence diagrams.
