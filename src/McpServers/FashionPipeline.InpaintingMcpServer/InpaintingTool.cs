using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FastMCP;
using FastMCP.Attributes;
using Microsoft.Extensions.Options;

namespace FashionPipeline.InpaintingMcpServer;

/// <summary>
/// Configuration for Virtual Try-On (VTON) Inpainting.
/// Supports 100% Free options (Hugging Face Gradio Spaces, Google Colab GPU)
/// as well as paid APIs (Replicate, Google Imagen Edit).
/// </summary>
public class VtonOptions
{
    /// <summary>
    /// Provider to use:
    ///   "HuggingFaceGradio" (Default, 100% Free, no credit card or token needed)
    ///   "Colab"             (Free, self-hosted on Colab/Kaggle T4 GPU with ngrok)
    ///   "Replicate"         (Paid API, requires ApiToken)
    /// </summary>
    public string Provider { get; set; } = "HuggingFaceGradio";

    /// <summary>
    /// For HuggingFaceGradio: The public HF Space URL.
    /// Default: "https://yisol-idm-vton.hf.space"
    /// Alternative: "https://levihsu-ootdiffusion.hf.space"
    /// </summary>
    public string GradioSpaceUrl { get; set; } = "https://yisol-idm-vton.hf.space";

    /// <summary>
    /// For Colab: The public ngrok/localtunnel URL exposed by your free Colab notebook.
    /// Example: "https://xyz123.ngrok-free.app"
    /// </summary>
    public string ColabUrl { get; set; } = "http://localhost:8000";

    /// <summary>Category: "dresses" | "upper_body" | "lower_body"</summary>
    public string GarmentCategory { get; set; } = "dresses";

    /// <summary>Inference steps (default: 20-30)</summary>
    public int NumInferenceSteps { get; set; } = 25;

    /// <summary>Guidance scale (default: 2.0)</summary>
    public float GuidanceScale { get; set; } = 2.0f;
}

public class ReplicateOptions
{
    public string ApiToken { get; set; } = string.Empty;
    public string ModelOwnerAndName { get; set; } = "cuuupid/idm-vton";
    public string ModelVersion { get; set; } = string.Empty;
    public int PollingTimeoutSeconds { get; set; } = 300;
}

public class ImagenEditOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/imagen-3.0-capability-001:edit";
    public bool EnableFallback { get; set; } = false;
}

/// <summary>
/// Applies an exact fashion accessory (lace/border/trim) onto a base fashion model image
/// using purpose-built Virtual Try-On (VTON) models (IDM-VTON / OOTDiffusion).
/// </summary>
public class InpaintingTool
{
    private readonly HttpClient _httpClient;
    private readonly VtonOptions _vtonOptions;
    private readonly ReplicateOptions _replicateOptions;
    private readonly ImagenEditOptions _imagenEditOptions;
    private readonly ILogger<InpaintingTool> _logger;

    public InpaintingTool(
        HttpClient httpClient,
        IOptions<VtonOptions> vtonOptions,
        IOptions<ReplicateOptions> replicateOptions,
        IOptions<ImagenEditOptions> imagenEditOptions,
        ILogger<InpaintingTool> logger)
    {
        _httpClient = httpClient;
        _vtonOptions = vtonOptions.Value;
        _replicateOptions = replicateOptions.Value;
        _imagenEditOptions = imagenEditOptions.Value;
        _logger = logger;
    }

    [McpTool("apply_accessory_to_dress", Description = "Applies the exact accessory design from the reference photo onto a base model image using Virtual Try-On (IDM-VTON / OOTDiffusion). Returns the path/URI of the final image.")]
    public async Task<string> ApplyAccessoryToDressAsync(
        string accessoryImageUri,
        string baseModelImageUri)
    {
        _logger.LogInformation(
            "InpaintingTool: Applying accessory '{Accessory}' onto base model '{BaseImage}' using provider '{Provider}'",
            accessoryImageUri, baseModelImageUri, _vtonOptions.Provider);

        var (accessoryBase64, accessoryMime) = await FetchImageDataAsync(accessoryImageUri);
        var (modelBase64, modelMime) = await FetchImageDataAsync(baseModelImageUri);

        var accessoryDataUri = $"data:{accessoryMime};base64,{accessoryBase64}";
        var modelDataUri = $"data:{modelMime};base64,{modelBase64}";

        byte[]? resultImageBytes = null;

        if (string.Equals(_vtonOptions.Provider, "Colab", StringComparison.OrdinalIgnoreCase))
        {
            resultImageBytes = await CallColabVtonAsync(accessoryDataUri, modelDataUri);
        }
        else if (string.Equals(_vtonOptions.Provider, "Replicate", StringComparison.OrdinalIgnoreCase))
        {
            resultImageBytes = await CallReplicateAsync(accessoryDataUri, modelDataUri);
        }
        else
        {
            // Default: Free Hugging Face Gradio Space (ZeroGPU, ₹0 cost)
            try
            {
                resultImageBytes = await CallHuggingFaceGradioAsync(accessoryDataUri, modelDataUri);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Free HuggingFace Gradio call failed. Checking fallbacks...");

                if (_imagenEditOptions.EnableFallback && !string.IsNullOrWhiteSpace(_imagenEditOptions.ApiKey))
                {
                    _logger.LogInformation("Falling back to Google Imagen 3 Edit API.");
                    resultImageBytes = await CallImagenEditAsync(accessoryBase64, accessoryMime, modelBase64, modelMime);
                }
                else
                {
                    throw;
                }
            }
        }

        if (resultImageBytes == null || resultImageBytes.Length == 0)
        {
            throw new InvalidOperationException("Virtual Try-On returned no image data.");
        }

        return await SaveResultAsync(resultImageBytes);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. FREE Hugging Face Gradio Space API (₹0 / No Token / No Card)
    // ──────────────────────────────────────────────────────────────────────────
    private async Task<byte[]> CallHuggingFaceGradioAsync(string accessoryDataUri, string modelDataUri)
    {
        var baseUrl = _vtonOptions.GradioSpaceUrl.TrimEnd('/');
        _logger.LogInformation("Calling Free HuggingFace Gradio Space at {BaseUrl}", baseUrl);

        // Gradio 4/5 call endpoint
        var callUrl = $"{baseUrl}/gradio_api/call/tryon";
        var payload = new
        {
            data = new object[]
            {
                new { background = new { url = modelDataUri }, layers = Array.Empty<object>(), composite = (object?)null },
                new { url = accessoryDataUri },
                "fashion lace border trim applied seamlessly to dress",
                true,
                true,
                _vtonOptions.NumInferenceSteps,
                42
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, callUrl);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(req);
        if (!response.IsSuccessStatusCode)
        {
            // Try legacy /api/predict endpoint
            var legacyUrl = $"{baseUrl}/api/predict";
            using var legacyReq = new HttpRequestMessage(HttpMethod.Post, legacyUrl);
            legacyReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var legacyResp = await _httpClient.SendAsync(legacyReq);
            if (legacyResp.IsSuccessStatusCode)
            {
                var legacyBody = await legacyResp.Content.ReadAsStringAsync();
                return await ParseGradioResultAsync(legacyBody, baseUrl);
            }

            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Hugging Face Gradio Space returned {response.StatusCode}: {err}");
        }

        var callRespBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(callRespBody);
        var eventId = doc.RootElement.GetProperty("event_id").GetString();

        // Stream or poll the result from /gradio_api/call/tryon/{eventId}
        var eventUrl = $"{baseUrl}/gradio_api/call/tryon/{eventId}";
        using var eventReq = new HttpRequestMessage(HttpMethod.Get, eventUrl);
        var eventResp = await _httpClient.SendAsync(eventReq, HttpCompletionOption.ResponseHeadersRead);
        
        using var reader = new StreamReader(await eventResp.Content.ReadAsStreamAsync());
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data:"))
            {
                var dataJson = line.Substring(5).Trim();
                if (!string.IsNullOrWhiteSpace(dataJson) && dataJson != "null")
                {
                    return await ParseGradioResultAsync(dataJson, baseUrl);
                }
            }
        }

        throw new InvalidOperationException("Hugging Face Gradio Space stream ended without output data.");
    }

    private async Task<byte[]> ParseGradioResultAsync(string jsonText, string baseUrl)
    {
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        // Check if data is array
        JsonElement dataEl = root.TryGetProperty("data", out var d) ? d : root;
        if (dataEl.ValueKind == JsonValueKind.Array && dataEl.GetArrayLength() > 0)
        {
            var first = dataEl[0];
            string? url = null;
            if (first.ValueKind == JsonValueKind.String)
                url = first.GetString();
            else if (first.TryGetProperty("url", out var uProp))
                url = uProp.GetString();
            else if (first.TryGetProperty("image", out var iProp) && iProp.TryGetProperty("url", out var iuProp))
                url = iuProp.GetString();

            if (!string.IsNullOrEmpty(url))
            {
                if (url.StartsWith("data:image"))
                {
                    var base64 = url.Substring(url.IndexOf(',') + 1);
                    return Convert.FromBase64String(base64);
                }

                if (!url.StartsWith("http"))
                    url = $"{baseUrl}/file={url}";

                return await _httpClient.GetByteArrayAsync(url);
            }
        }

        throw new InvalidOperationException($"Could not extract image from Gradio response: {jsonText}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Free Google Colab / Self-Hosted Endpoint
    // ──────────────────────────────────────────────────────────────────────────
    private async Task<byte[]> CallColabVtonAsync(string accessoryDataUri, string modelDataUri)
    {
        var url = $"{_vtonOptions.ColabUrl.TrimEnd('/')}/tryon";
        _logger.LogInformation("Calling Colab VTON server at {Url}", url);

        var payload = new
        {
            model_image = modelDataUri,
            garment_image = accessoryDataUri,
            category = _vtonOptions.GarmentCategory,
            steps = _vtonOptions.NumInferenceSteps
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(req);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Colab VTON server returned {response.StatusCode}: {err}");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.StartsWith("image/"))
        {
            return await response.Content.ReadAsByteArrayAsync();
        }

        var respString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respString);
        if (doc.RootElement.TryGetProperty("image_base64", out var b64))
        {
            return Convert.FromBase64String(b64.GetString()!);
        }

        throw new InvalidOperationException($"Unexpected response from Colab server: {respString}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Replicate API (Paid fallback)
    // ──────────────────────────────────────────────────────────────────────────
    private async Task<byte[]> CallReplicateAsync(string accessoryDataUri, string modelDataUri)
    {
        if (string.IsNullOrWhiteSpace(_replicateOptions.ApiToken))
            throw new InvalidOperationException("Replicate:ApiToken is not configured.");

        var inputPayload = new
        {
            human_img = modelDataUri,
            garm_img = accessoryDataUri,
            garment_des = "fashion lace border trim accessory to apply precisely along the dress",
            category = _vtonOptions.GarmentCategory,
            num_inference_steps = _vtonOptions.NumInferenceSteps,
            guidance_scale = _vtonOptions.GuidanceScale,
            is_checked = true,
            is_checked_crop = false,
            denoise_steps = 40
        };

        var predictionUrl = $"https://api.replicate.com/v1/models/{_replicateOptions.ModelOwnerAndName}/predictions";
        var json = JsonSerializer.Serialize(new { input = inputPayload });

        using var req = new HttpRequestMessage(HttpMethod.Post, predictionUrl);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Token", _replicateOptions.ApiToken);
        req.Headers.TryAddWithoutValidation("Prefer", "wait");

        var resp = await _httpClient.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Replicate error: {body}");

        using var doc = JsonDocument.Parse(body);
        var predId = doc.RootElement.GetProperty("id").GetString()!;
        var status = doc.RootElement.GetProperty("status").GetString();

        if (status == "succeeded")
        {
            var outUrl = doc.RootElement.GetProperty("output").GetString()!;
            return await _httpClient.GetByteArrayAsync(outUrl);
        }

        // Poll
        var pollUrl = $"https://api.replicate.com/v1/predictions/{predId}";
        var timeout = DateTime.UtcNow.AddSeconds(_replicateOptions.PollingTimeoutSeconds);
        while (DateTime.UtcNow < timeout)
        {
            await Task.Delay(3000);
            using var pReq = new HttpRequestMessage(HttpMethod.Get, pollUrl);
            pReq.Headers.Authorization = new AuthenticationHeaderValue("Token", _replicateOptions.ApiToken);
            var pResp = await _httpClient.SendAsync(pReq);
            var pBody = await pResp.Content.ReadAsStringAsync();
            using var pDoc = JsonDocument.Parse(pBody);
            var pStatus = pDoc.RootElement.GetProperty("status").GetString();
            if (pStatus == "succeeded")
            {
                var outUrl = pDoc.RootElement.GetProperty("output").GetString()!;
                return await _httpClient.GetByteArrayAsync(outUrl);
            }
            if (pStatus == "failed" || pStatus == "canceled")
                throw new InvalidOperationException($"Replicate {pStatus}");
        }

        throw new TimeoutException("Replicate timed out.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Google Imagen 3 Edit API (Paid fallback)
    // ──────────────────────────────────────────────────────────────────────────
    private async Task<byte[]> CallImagenEditAsync(
        string accessoryBase64, string accessoryMime,
        string modelBase64, string modelMime)
    {
        var requestBody = new
        {
            instances = new[]
            {
                new
                {
                    prompt = "Apply the exact lace border/trim from the reference accessory image precisely along the hemlines and neckline of the dress.",
                    referenceImages = new[]
                    {
                        new
                        {
                            referenceType = "REFERENCE_TYPE_SUBJECT",
                            referenceId = 1,
                            referenceImage = new { bytesBase64Encoded = accessoryBase64, mimeType = accessoryMime }
                        }
                    },
                    image = new { bytesBase64Encoded = modelBase64, mimeType = modelMime }
                }
            },
            parameters = new { sampleCount = 1, editConfig = new { editMode = "EDIT_MODE_INPAINT_INSERTION" } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_imagenEditOptions.Endpoint}?key={_imagenEditOptions.ApiKey}");
        req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(req);
        var respText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Imagen Edit API error: {respText}");

        using var doc = JsonDocument.Parse(respText);
        var b64Str = doc.RootElement.GetProperty("predictions")[0].GetProperty("bytesBase64Encoded").GetString()!;
        return Convert.FromBase64String(b64Str);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────
    private async Task<string> SaveResultAsync(byte[] imageBytes)
    {
        var outFileName = $"{Guid.NewGuid()}.png";
        var outFolder = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(outFolder);
        var filePath = Path.Combine(outFolder, outFileName);
        await File.WriteAllBytesAsync(filePath, imageBytes);

        _logger.LogInformation("InpaintingTool: Final VTON image saved to {Path}", filePath);
        return $"file:///{filePath.Replace('\\', '/')}";
    }

    private async Task<(string base64, string mimeType)> FetchImageDataAsync(string imageUrl)
    {
        if (imageUrl.StartsWith("file://"))
        {
            var localPath = imageUrl.Replace("file:///", "").Replace("file://", "");
            if (File.Exists(localPath))
            {
                var fileBytes = await File.ReadAllBytesAsync(localPath);
                return (Convert.ToBase64String(fileBytes), GetMime(localPath));
            }
        }

        if (File.Exists(imageUrl))
        {
            var fileBytes = await File.ReadAllBytesAsync(imageUrl);
            return (Convert.ToBase64String(fileBytes), GetMime(imageUrl));
        }

        if (imageUrl.Contains("/uploads/"))
        {
            var fileName = imageUrl.Substring(imageUrl.LastIndexOf("/uploads/") + 9);
            var localUploadPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "FashionPipeline.Api", "wwwroot", "uploads", fileName);
            if (File.Exists(localUploadPath))
            {
                var fileBytes = await File.ReadAllBytesAsync(localUploadPath);
                return (Convert.ToBase64String(fileBytes), GetMime(fileName));
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to download image: {imageUrl}");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        return (Convert.ToBase64String(bytes), contentType);
    }

    private static string GetMime(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
}
