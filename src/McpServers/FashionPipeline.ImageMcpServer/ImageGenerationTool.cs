using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using FastMCP;
using FastMCP.Attributes;
using Microsoft.Extensions.Options;
using FashionPipeline.Core.Options;

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
    private readonly AiProviderOptions _providerOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<ImageGenerationTool> _logger;

    public ImageGenerationTool(
        HttpClient httpClient,
        IOptions<ImagenOptions> imagenOptions,
        IOptions<AiProviderOptions> providerOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<ImageGenerationTool> logger)
    {
        _httpClient = httpClient;
        _imagenOptions = imagenOptions.Value;
        _providerOptions = providerOptions.Value;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates the base fashion model image from the prompt (Step 3 of the pipeline).
    /// This produces a clean, high-resolution portrait of an Indian model in the chosen setting.
    /// The accessory is then applied precisely onto this base model image in Step 4 via InpaintingMcpServer.
    /// </summary>
    [McpTool("generate_accessory_image", Description = "Generates the base fashion model image from the text prompt for downstream Virtual Try-On inpainting.")]
    public async Task<string> GenerateImageAsync(string prompt, string rawImageUri)
    {
        _logger.LogInformation("ImageGenerationTool: Generating base model image for rawImageUri '{RawImageUri}'", rawImageUri);

        var isAzure = string.Equals(_providerOptions.Provider, "Azure", StringComparison.OrdinalIgnoreCase);

        if (isAzure)
        {
            if (string.IsNullOrWhiteSpace(_providerOptions.Azure?.ApiKey) || string.IsNullOrWhiteSpace(_providerOptions.Azure?.Endpoint))
            {
                throw new InvalidOperationException("Provider is configured as 'Azure', but Azure ApiKey or Endpoint is missing in appsettings.");
            }

            var azureResult = await GenerateImageViaAzureAsync(prompt, rawImageUri);
            if (!string.IsNullOrEmpty(azureResult))
            {
                return azureResult;
            }

            throw new InvalidOperationException("Azure FLUX image generation failed.");
        }

        // Google Gemini path — sends the accessory image as inline_data alongside the prompt
        // so the model can see the exact design and replicate it faithfully.
        return await GenerateImageViaGoogleAsync(prompt, rawImageUri);
    }

    /// <summary>
    /// Calls the Azure AI Foundry FLUX / OpenAI image generation endpoint.
    /// Passes the reference accessory image conditioning (input_image) alongside the prompt.
    /// </summary>
    private async Task<string> GenerateImageViaAzureAsync(string prompt, string rawImageUri)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt must not be empty for image generation.", nameof(prompt));

        var azureOpt = _providerOptions.Azure;
        var baseEndpoint = azureOpt.Endpoint.TrimEnd('/');
        if (baseEndpoint.Contains("/api/projects/"))
        {
            baseEndpoint = baseEndpoint.Substring(0, baseEndpoint.IndexOf("/api/projects/"));
        }

        var modelName = !string.IsNullOrEmpty(azureOpt.ImageDeployment) ? azureOpt.ImageDeployment : "FLUX.2-pro";

        var fluxUrl = !string.IsNullOrWhiteSpace(azureOpt.FluxEndpoint)
            ? azureOpt.FluxEndpoint
            : $"{baseEndpoint}/openai/v1/images/generations";

        var fluxKey = !string.IsNullOrWhiteSpace(azureOpt.FluxApiKey)
            ? azureOpt.FluxApiKey
            : azureOpt.ApiKey;

        var width = azureOpt.ImageWidth > 0 ? azureOpt.ImageWidth : 1024;
        var height = azureOpt.ImageHeight > 0 ? azureOpt.ImageHeight : 1792;
        var sizeStr = $"{width}x{height}";

        _logger.LogInformation("ImageMcp: Generating via Azure {Model} at {Url} (size={Size})", modelName, fluxUrl, sizeStr);

        string? imageBase64 = null;
        if (!string.IsNullOrWhiteSpace(rawImageUri))
        {
            try
            {
                var (b64, _) = await FetchImageDataAsync(rawImageUri);
                imageBase64 = b64;
                _logger.LogInformation("ImageMcp: Attached reference accessory image ({Length} chars base64)", imageBase64.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ImageMcp: Could not load reference image from {Uri}. Proceeding with text-only prompt.", rawImageUri);
            }
        }

        var fluxBody = new Dictionary<string, object>
        {
            ["model"] = modelName,
            ["prompt"] = prompt,
            ["size"] = sizeStr,
            ["n"] = 1
        };

        if (!string.IsNullOrEmpty(imageBase64))
        {
            fluxBody["input_image"] = imageBase64;
            fluxBody["image"] = imageBase64;
        }

        var fluxJson = JsonSerializer.Serialize(fluxBody);

        using var req = new HttpRequestMessage(HttpMethod.Post, fluxUrl);
        req.Content = new StringContent(fluxJson, Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("api-key", fluxKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fluxKey);

        var fluxResponse = await _httpClient.SendAsync(req);
        var fluxRespText = await fluxResponse.Content.ReadAsStringAsync();

        if (!fluxResponse.IsSuccessStatusCode)
        {
            _logger.LogError("{Model} returned {Status}: {Error}", modelName, fluxResponse.StatusCode, fluxRespText);
            throw new HttpRequestException($"{modelName} API error (Status {fluxResponse.StatusCode}): {fluxRespText}");
        }

        byte[]? imageBytes = null;
        using var fluxDoc = JsonDocument.Parse(fluxRespText);
        if (fluxDoc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
        {
            var first = dataArr[0];

            if (first.TryGetProperty("b64_json", out var b64Prop))
            {
                var b64 = b64Prop.GetString();
                if (!string.IsNullOrEmpty(b64))
                    imageBytes = Convert.FromBase64String(b64);
            }

            if (imageBytes == null && first.TryGetProperty("url", out var urlProp))
            {
                var imgUrl = urlProp.GetString();
                if (!string.IsNullOrEmpty(imgUrl))
                    imageBytes = await _httpClient.GetByteArrayAsync(imgUrl);
            }
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            _logger.LogError("{Model} response did not contain image data. Full response: {Response}", modelName, fluxRespText);
            throw new InvalidOperationException($"{modelName} returned a success status but no image data was found in the response.");
        }

        return await SaveImageBytesAsync(imageBytes, modelName);
    }

    /// <summary>
    /// Calls Gemini's generateContent endpoint with BOTH the text prompt AND the accessory
    /// reference image as inline_data. This replicates what you do manually in Gemini web:
    ///
    ///   1. Upload the accessory photo (lace/border image)
    ///   2. Type the prompt describing the desired composite layout
    ///   3. Gemini generates the final image with the accessory design faithfully replicated
    ///
    /// The key difference from the old code: the old version sent ONLY the text prompt.
    /// This version sends both the image AND the text, so Gemini can actually SEE the
    /// exact lace pattern and replicate it in the generated output.
    ///
    /// IMPORTANT: Use an image-generation-capable model endpoint:
    ///   - gemini-2.0-flash-preview-image-generation:generateContent  (works)
    ///   - gemini-2.0-flash:generateContent                          (may work if image gen enabled)
    ///   - NOT a text-only endpoint
    /// </summary>
    private async Task<string> GenerateImageViaGoogleAsync(string prompt, string rawImageUri)
    {
        var apiKey = !string.IsNullOrWhiteSpace(_providerOptions.Google?.ApiKey)
            ? _providerOptions.Google.ApiKey
            : _imagenOptions.ApiKey;

        var endpoint = !string.IsNullOrWhiteSpace(_providerOptions.Google?.ImagenEndpoint)
            ? _providerOptions.Google.ImagenEndpoint
            : _imagenOptions.Endpoint;

        _logger.LogInformation(
            "ImageGenerationTool: Generating image via Google endpoint '{Endpoint}' with accessory image '{ImageUri}'",
            endpoint, rawImageUri);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Google/Gemini ApiKey is not configured in appsettings.json.");
        }

        // Fetch the raw accessory image and convert to base64
        var (imageBase64, imageMimeType) = await FetchImageDataAsync(rawImageUri);
        _logger.LogInformation(
            "ImageGenerationTool: Loaded accessory image ({Size} chars base64, mime={Mime})",
            imageBase64.Length, imageMimeType);

        // Build the Gemini multimodal request with BOTH the accessory image AND the text prompt
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            inline_data = new
                            {
                                mime_type = imageMimeType,
                                data = imageBase64
                            }
                        },
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "IMAGE", "TEXT" }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);

        HttpResponseMessage response = null!;
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                response = await _httpClient.PostAsync($"{endpoint}?key={apiKey}", content);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 4)
                {
                    int delayMs = attempt * 20_000;
                    _logger.LogWarning("Rate limited (429). Retry {Attempt}/4 after {Delay}ms", attempt, delayMs);
                    await Task.Delay(delayMs);
                    continue;
                }
                break;
            }
            catch (Exception ex) when (attempt < 4)
            {
                _logger.LogWarning(ex, "HTTP request failed on attempt {Attempt}/4. Retrying in 5s...", attempt);
                await Task.Delay(5000);
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            var errText = response != null ? await response.Content.ReadAsStringAsync() : "No response received";
            throw new HttpRequestException($"Google Image API error (Status {response?.StatusCode}): {errText}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        string? generatedBase64 = null;
        string outputMime = "image/png";

        // Format A: Gemini generateContent — try both camelCase (inlineData) and snake_case (inline_data)
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var parts = candidates[0].GetProperty("content").GetProperty("parts");
            foreach (var part in parts.EnumerateArray())
            {
                // Try camelCase first (actual Gemini API response format)
                JsonElement inlineData;
                bool found = part.TryGetProperty("inlineData", out inlineData)
                          || part.TryGetProperty("inline_data", out inlineData);

                if (found)
                {
                    generatedBase64 = inlineData.GetProperty("data").GetString();
                    // Try both mimeType and mime_type
                    JsonElement mt;
                    if (inlineData.TryGetProperty("mimeType", out mt) || inlineData.TryGetProperty("mime_type", out mt))
                        outputMime = mt.GetString() ?? "image/png";
                    break;
                }
            }
        }

        // Format B: Imagen predict (predictions[0].bytesBase64Encoded)
        else if (doc.RootElement.TryGetProperty("predictions", out var preds) && preds.GetArrayLength() > 0)
        {
            var first = preds[0];
            if (first.TryGetProperty("bytesBase64Encoded", out var b64Prop))
            {
                generatedBase64 = b64Prop.GetString();
                if (first.TryGetProperty("mimeType", out var mtProp))
                    outputMime = mtProp.GetString() ?? "image/png";
            }
        }

        if (string.IsNullOrWhiteSpace(generatedBase64))
        {
            throw new InvalidOperationException("Google API response did not contain a generated image. Full Response: " + responseJson);
        }

        var imageBytes = Convert.FromBase64String(generatedBase64);
        var extension = outputMime.Contains("png") ? ".png" : ".webp";
        var fileName = $"{Guid.NewGuid()}{extension}";

        if (!string.IsNullOrWhiteSpace(_storageOptions.ConnectionString))
        {
            var blobName = $"images/{fileName}";
            var blobClient = new BlobContainerClient(_storageOptions.ConnectionString, _storageOptions.ContainerName);
            await blobClient.CreateIfNotExistsAsync();
            var blob = blobClient.GetBlobClient(blobName);
            await blob.UploadAsync(new BinaryData(imageBytes), overwrite: true);
            return blob.Uri.ToString();
        }
        else
        {
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
            Directory.CreateDirectory(outputDir);
            var filePath = Path.Combine(outputDir, fileName);
            await File.WriteAllBytesAsync(filePath, imageBytes);
            _logger.LogInformation("Final composite image saved to {Path}", filePath);
            return $"file:///{filePath.Replace('\\', '/')}";
        }
    }

    private async Task<string> SaveImageBytesAsync(byte[] bytes, string providerName)
    {
        var outFileName = $"{Guid.NewGuid()}.png";
        var outFolder = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(outFolder);
        var filePath = Path.Combine(outFolder, outFileName);
        await File.WriteAllBytesAsync(filePath, bytes);

        _logger.LogInformation("Saved {Provider} image to {Path}", providerName, filePath);
        return $"file:///{filePath.Replace('\\', '/')}";
    }

    /// <summary>
    /// Fetches image from various sources: local file://, uploads directory, or HTTP URL.
    /// Returns the image as (base64, mimeType) for use in Gemini inline_data.
    /// </summary>
    private async Task<(string base64, string mimeType)> FetchImageDataAsync(string imageUrl)
    {
        if (imageUrl.StartsWith("file://") || File.Exists(imageUrl))
        {
            var localPath = imageUrl.Replace("file:///", "").Replace("file://", "");
            if (File.Exists(localPath))
            {
                var fileBytes = await File.ReadAllBytesAsync(localPath);
                var mime = localPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                         : localPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                         : "image/jpeg";
                return (Convert.ToBase64String(fileBytes), mime);
            }
        }

        if (imageUrl.Contains("/uploads/"))
        {
            var fileName = imageUrl.Substring(imageUrl.LastIndexOf("/uploads/") + 9);
            var localUploadPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "FashionPipeline.Api", "wwwroot", "uploads", fileName);
            if (File.Exists(localUploadPath))
            {
                var fileBytes = await File.ReadAllBytesAsync(localUploadPath);
                var mime = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                         : fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                         : "image/jpeg";
                return (Convert.ToBase64String(fileBytes), mime);
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to download image from {imageUrl} (Status {response.StatusCode}): {err}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        return (Convert.ToBase64String(bytes), contentType);
    }
}