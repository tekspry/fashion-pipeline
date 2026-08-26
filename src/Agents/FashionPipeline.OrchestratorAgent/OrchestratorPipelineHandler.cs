using System.Text.Json;
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;
using FashionPipeline.Core.Jobs;
using FashionPipeline.Core.Services;
using FashionPipeline.OrchestratorAgent.A2A;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FashionPipeline.OrchestratorAgent;

public sealed class OrchestratorPipelineHandler
{
    private readonly AppDbContext _db;
    private readonly A2AAgentClient _a2a;
    private readonly ICatalogPublishService _catalog;
    private readonly ILogger<OrchestratorPipelineHandler> _logger;

    public OrchestratorPipelineHandler(
        AppDbContext db,
        A2AAgentClient a2a,
        ICatalogPublishService catalog,
        ILogger<OrchestratorPipelineHandler> logger)
    {
        _db = db;
        _a2a = a2a;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<string> RunAsync(string inboundText, CancellationToken cancellationToken)
    {
        var payload = OrchestratorMessageParser.Parse(inboundText);
        _db.CurrentTenantId = payload.TenantId;
        var accessory = await _db.Accessories.FindAsync(new object[] { payload.AccessoryId }, cancellationToken)
            ?? throw new InvalidOperationException($"Accessory {payload.AccessoryId} was not found.");
        var imageUrl = string.IsNullOrWhiteSpace(payload.ImageUrl)
            ? accessory.RawImageUri
            : payload.ImageUrl;
        accessory.Status = AccessoryStatus.Processing;
        await _db.SaveChangesAsync(cancellationToken);

        // ==========================================
        // STEP 1: Feature Extraction
        // ==========================================
        string featureJson;
        try
        {
            featureJson = await ResolveFeaturesAsync(accessory, imageUrl, cancellationToken);
            _logger.LogInformation("Step 1 (Feature Extraction) complete for accessory {AccessoryId}", accessory.Id);

            // Mark Step 1 complete & persist to SQLite immediately so Step 1 data is preserved
            accessory.Status = AccessoryStatus.Complete;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 1 (Feature Extraction) failed for accessory {AccessoryId}", accessory.Id);
            accessory.Status = AccessoryStatus.Failed;
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }

        // ==========================================
        // STEP 2: Prompt Generation (Isolated & Independent)
        // ==========================================
        IReadOnlyList<string> prompts = Array.Empty<string>();
        string step2StatusMessage = string.Empty;
        try
        {
            _logger.LogInformation("Starting Step 2 (Prompt Generation) for accessory {AccessoryId}", accessory.Id);

            var creativePayload = JsonSerializer.Serialize(new { featureJson, imageUrl });
            var promptsJson = await _a2a.SendToCreativeAsync(creativePayload, cancellationToken);
            prompts = ParsePrompts(promptsJson);
            _logger.LogInformation("Step 2 (Prompt Generation) complete for accessory {AccessoryId}. Generated {Count} prompts.", accessory.Id, prompts.Count);
            step2StatusMessage = $"Step 2 (Prompt Generation): Success ({prompts.Count} prompts generated).";
        }
        catch (Exception ex)
        {
            // Step 2 failure does NOT break Step 1 — Step 1 features are safely saved in DB
            _logger.LogError(ex, "Step 2 (Prompt Generation) failed for accessory {AccessoryId}. Step 1 features remain preserved.", accessory.Id);
            step2StatusMessage = $"Step 2 (Prompt Generation): Failed ({ex.Message}). Step 1 features preserved.";
        }

        // ==========================================
        // STEP 3: Base Model Image Generation
        // Generates clean base fashion model images using the prompts.
        // ==========================================
        var generatedBaseImageUrls = new List<string>();
        string step3StatusMessage = string.Empty;

        if (prompts.Count > 0)
        {
            try
            {
                _logger.LogInformation("Starting Step 3 (Base Image Generation) for accessory {AccessoryId} ({Count} prompts)", accessory.Id, prompts.Count);

                foreach (var prompt in prompts)
                {
                    // Check cache first
                    var cachedImage = await _db.GeneratedAssets.IgnoreQueryFilters().FirstOrDefaultAsync(
                        g => g.AccessoryId == accessory.Id
                             && g.AssetType == "BaseImage"
                             && g.PromptUsed == prompt,
                        cancellationToken);

                    if (cachedImage is not null)
                    {
                        generatedBaseImageUrls.Add(cachedImage.AssetUri);
                        _logger.LogInformation("Base image cache hit for accessory {AccessoryId}", accessory.Id);
                        continue;
                    }

                    // Send prompt to ImageAgent to generate clean base model image
                    var imageRequest = JsonSerializer.Serialize(new { prompt, rawImageUri = imageUrl });
                    var baseImageUrl = await _a2a.SendToImageAsync(imageRequest, cancellationToken);

                    // Persist final composite image as authoritative "Image" asset and "BaseImage"
                    await _catalog.PublishAsync(
                        payload.TenantId, accessory.Id, "Image", baseImageUrl, prompt, cancellationToken);
                    await _catalog.PublishAsync(
                        payload.TenantId, accessory.Id, "BaseImage", baseImageUrl, prompt, cancellationToken);

                    generatedBaseImageUrls.Add(baseImageUrl);
                }

                accessory.Status = AccessoryStatus.Complete;
                await _db.SaveChangesAsync(cancellationToken);
                step3StatusMessage = $"Step 3 (Base Image Generation): Success ({generatedBaseImageUrls.Count} base images generated).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step 3 (Base Image Generation) failed for accessory {AccessoryId}. Steps 1 & 2 remain preserved.", accessory.Id);
                step3StatusMessage = $"Step 3 (Base Image Generation): Failed ({ex.Message}). Step 1 & 2 outputs preserved.";
            }
        }
        else
        {
            step3StatusMessage = "Step 3 (Base Image Generation): Skipped (No prompts generated in Step 2).";
        }

        // ==========================================
        // STEP 4: Virtual Try-On Inpainting
        // Takes the base model image + the original accessory photo and applies the
        // exact physical accessory design using Virtual Try-On (IDM-VTON / OOTDiffusion).
        // ==========================================
        var finalImageUrls = new List<string>();
        string step4StatusMessage = string.Empty;

        if (generatedBaseImageUrls.Count > 0)
        {
            try
            {
                _logger.LogInformation("Starting Step 4 (Virtual Try-On Inpainting) for accessory {AccessoryId} ({Count} base images)", accessory.Id, generatedBaseImageUrls.Count);

                foreach (var baseImageUrl in generatedBaseImageUrls)
                {
                    var inpaintRequest = JsonSerializer.Serialize(new
                    {
                        accessoryImageUri = imageUrl,      // Original accessory photo
                        baseModelImageUri = baseImageUrl   // AI-generated base model image from Step 3
                    });

                    var finalImageUrl = await _a2a.SendToInpaintingAsync(inpaintRequest, cancellationToken);

                    // Persist the final VTON inpainted image as authoritative "Image" asset
                    await _catalog.PublishAsync(
                        payload.TenantId, accessory.Id, "Image", finalImageUrl, "Virtual Try-On (VTON) inpainting result", cancellationToken);

                    finalImageUrls.Add(finalImageUrl);
                }

                accessory.Status = AccessoryStatus.Complete;
                await _db.SaveChangesAsync(cancellationToken);
                step4StatusMessage = $"Step 4 (Virtual Try-On Inpainting): Success ({finalImageUrls.Count} final images generated).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step 4 (Virtual Try-On Inpainting) failed for accessory {AccessoryId}. Steps 1-3 remain preserved.", accessory.Id);
                step4StatusMessage = $"Step 4 (Virtual Try-On Inpainting): Failed ({ex.Message}). Base images from Step 3 preserved.";
            }
        }
        else
        {
            step4StatusMessage = "Step 4 (Virtual Try-On Inpainting): Skipped (No base images generated in Step 3).";
        }

        var formattedPrompts = prompts.Count > 0
            ? string.Join("\n\n", prompts.Select((p, i) => $"[Prompt {i + 1}]:\n{p}"))
            : "(No prompts generated)";

        var formattedBaseImages = generatedBaseImageUrls.Count > 0
            ? string.Join("\n", generatedBaseImageUrls.Select((url, i) => $"[Base Image {i + 1}]: {url}"))
            : "(No base images generated)";

        var formattedFinalImages = finalImageUrls.Count > 0
            ? string.Join("\n", finalImageUrls.Select((url, i) => $"[Final Image {i + 1}]: {url}"))
            : "(No final images generated)";

        return $"=== PIPELINE SUMMARY ===\n\n" +
               $"Step 1 (Feature Extraction): Success\nExtracted Features:\n{featureJson}\n\n" +
               $"{step2StatusMessage}\n\nPrompts:\n{formattedPrompts}\n\n" +
               $"{step3StatusMessage}\n\nBase Images:\n{formattedBaseImages}\n\n" +
               $"{step4StatusMessage}\n\nFinal Images (Virtual Try-On):\n{formattedFinalImages}";
    }

    private async Task<string> ResolveFeaturesAsync(
        Accessory accessory, string imageUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(accessory.ExtractedFeatures))
            return accessory.ExtractedFeatures;

        if (!string.IsNullOrWhiteSpace(accessory.ImageHash))
        {
            var cachedByHash = await _db.Accessories
                .AsNoTracking()
                .Where(a => a.ImageHash == accessory.ImageHash
                            && a.ExtractedFeatures != null
                            && a.ExtractedFeatures != string.Empty)
                .Select(a => a.ExtractedFeatures!)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(cachedByHash))
            {
                accessory.ExtractedFeatures = cachedByHash;
                await _db.SaveChangesAsync(cancellationToken);
                return cachedByHash;
            }
        }

        var visionRequest = JsonSerializer.Serialize(new { imageUrl });
        var featureJson = await _a2a.SendToVisionAsync(visionRequest, cancellationToken);

        accessory.ExtractedFeatures = featureJson;
        await _db.SaveChangesAsync(cancellationToken);
        return featureJson;
    }

    private static IReadOnlyList<string> ParsePrompts(string promptsJson)
    {
        promptsJson = promptsJson.Trim();
        if (string.IsNullOrWhiteSpace(promptsJson))
            return Array.Empty<string>();

        using var doc = JsonDocument.Parse(promptsJson);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        return new[] { promptsJson };
    }
}