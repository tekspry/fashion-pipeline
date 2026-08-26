using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FastMCP;
using FastMCP.Attributes;
using Microsoft.Extensions.Options;
using FashionPipeline.Core.Options;

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
    private readonly AiProviderOptions _providerOptions;
    private readonly ILogger<FeatureExtractionTool> _logger;

    public FeatureExtractionTool(
        HttpClient httpClient, 
        IOptions<GeminiOptions> options, 
        IOptions<AiProviderOptions> providerOptions,
        ILogger<FeatureExtractionTool> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _providerOptions = providerOptions.Value;
        _logger = logger;
    }

    [McpTool("extract_accessory_features", Description = "Calls Gemini or Azure Vision to extract JSON features from an image URL")]
    public async Task<string> ExtractFeaturesAsync(string imageUrl)
    {
        var isAzure = string.Equals(_providerOptions.Provider, "Azure", StringComparison.OrdinalIgnoreCase);

        if (isAzure)
        {
            if (string.IsNullOrWhiteSpace(_providerOptions.Azure?.ApiKey) || string.IsNullOrWhiteSpace(_providerOptions.Azure?.Endpoint))
            {
                throw new InvalidOperationException("Provider is configured as 'Azure', but Azure ApiKey or Endpoint is missing in appsettings.");
            }
            return await ExtractFeaturesViaAzureAsync(imageUrl);
        }

        return await ExtractFeaturesViaGoogleAsync(imageUrl);
    }

    private const string SharedVisionPromptText = @"
now identify the exact color and describe precise design features for this newly uploaded fashion accessory image (button, lace, border, trim, brooch, or embellishment) generally applied on front of ladies kurti, ladies suits of Indian ladies dress. Inspect the image closely and extract exact material, color, surface texture, silhouette, profile, and craftsmanship details.

Return ONLY a JSON object containing the following keys:
- Title: A striking, descriptive name capturing the exact color, material, pattern/silhouette, and dimension (e.g. ""1-Inch Textured Champagne Gold Starfish Statement Button"" or ""2.5-Inch Champagne Gold Fish-Scale Mirror Border"").
- ColorIdentification: An object with keys:
    - PrimaryFinish: Exact primary finish, color (e.g. Polished Champagne Gold / Light Gold), and luster/metallic shine.
    - ReflectiveUndertones: Shadow undertones, reflectivity, and secondary highlights (e.g. Smoky Gunmetal / Deep Bronze shadows).
    - BaseMaterial: Foundation material, plating, or thread.
- PreciseDesignFeatures: An object with keys describing key design and structural attributes (e.g. ProportionalProfile, Silhouette, Texture, Perimeter, Form, BackingOrShank, Craftsmanship).
- SuggestedApplications: An array of strings covering specific Indian ladies dress applications (e.g. Kurti Front Plackets, Sleeve Cuff Accents, Necklines, Saree Borders, Dupatta Framing).
- Color: Summary string of exact colors and tones.
- Type: Summary string of exact type and dimension.
- Material: Summary string of materials, finish, and texture.
- Vibe: Summary string of aesthetic vibe.
- Style: Summary string of styling compatibility.
- Features: Detailed bulleted or paragraph summary of all precise design features.";

    private async Task<string> ExtractFeaturesViaAzureAsync(string imageUrl)
    {
        // --- Guardrail: validate input ---
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new ArgumentException("imageUrl must not be empty for feature extraction.", nameof(imageUrl));

        var azureOpt = _providerOptions.Azure;
        var model = !string.IsNullOrEmpty(azureOpt.VisionDeployment) ? azureOpt.VisionDeployment : "gpt-5.6-sol";

        _logger.LogInformation("VisionMcp: Extracting features via Azure ({Model}) for {ImageUrl}", model, imageUrl);
        _logger.LogInformation("⚠️  COST ALERT: This call uses {Model} (gpt-5.6-sol ≈ ₹1.80/run). Making exactly 1 API call.", model);

        var (base64Data, mimeType) = await FetchImageDataAsync(imageUrl);
        var dataUri = $"data:{mimeType};base64,{base64Data}";

        // --- Guardrail: cap base64 payload to avoid oversized requests ---
        if (base64Data.Length > 5_000_000)
            throw new InvalidOperationException($"Image payload exceeds 5MB base64 limit ({base64Data.Length} chars). Resize the image before processing.");

        var promptText = LoadVisionTemplate();

        var requestBody = new
        {
            model = model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = promptText },
                        new { type = "image_url", image_url = new { url = dataUri } }
                    }
                }
            },
            max_completion_tokens = 1024,
            temperature = 1,
            response_format = new { type = "json_object" }
        };

        var baseEndpoint = azureOpt.Endpoint.TrimEnd('/');
        if (baseEndpoint.Contains("/api/projects/"))
        {
            baseEndpoint = baseEndpoint.Substring(0, baseEndpoint.IndexOf("/api/projects/"));
        }
        var fullUrl = $"{baseEndpoint}/openai/v1/chat/completions";

        using var req = new HttpRequestMessage(HttpMethod.Post, fullUrl);
        req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        // Azure AI Foundry uses Bearer auth (not api-key header)
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", azureOpt.ApiKey);

        var response = await _httpClient.SendAsync(req);
        var respText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Azure {model} Vision API error (Status {response.StatusCode}): {respText}");
        }

        using var doc = JsonDocument.Parse(respText);
        var rawText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return CleanJsonText(rawText);
    }

    private async Task<string> ExtractFeaturesViaGoogleAsync(string imageUrl)
    {
        _logger.LogInformation("VisionMcp: Extracting features for {ImageUrl} using endpoint '{Endpoint}' and key starting with '{KeyStart}' (len: {KeyLen})",
            imageUrl, _options.VisionEndpoint, _options.ApiKey.Length > 10 ? _options.ApiKey.Substring(0, 10) : _options.ApiKey, _options.ApiKey.Length);

        var (base64Data, mimeType) = await FetchImageDataAsync(imageUrl);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = LoadVisionTemplate() },
                        new { inline_data = new { mime_type = mimeType, data = base64Data } }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);        
        HttpResponseMessage response = null!;
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var fullUrl = $"{_options.VisionEndpoint}?key={_options.ApiKey}";
                _logger.LogInformation("VisionMcp: Sending HTTP POST to {UrlMasked}", $"{_options.VisionEndpoint}?key=***");
                response = await _httpClient.PostAsync(fullUrl, content);
                
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 4)
                {
                    int delayMs = attempt * 15000; // 15s for attempt 1, 30s for attempt 2, 45s for attempt 3
                    await Task.Delay(delayMs);
                    continue;
                }
                break;
            }
            catch (Exception) when (attempt < 4)
            {
                await Task.Delay(5000);
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            var errorContent = response != null ? await response.Content.ReadAsStringAsync() : "No response received";
            throw new HttpRequestException($"Gemini API error (Status {response?.StatusCode}): {errorContent}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        
        // Extract the text from the Gemini response
        var rawText = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        return CleanJsonText(rawText);
    }

    private static string CleanJsonText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "{}";
        var rawText = text.Trim();
        if (rawText.StartsWith("```json"))
            rawText = rawText.Substring(7);
        else if (rawText.StartsWith("```"))
            rawText = rawText.Substring(3);
        if (rawText.EndsWith("```"))
            rawText = rawText.Substring(0, rawText.Length - 3);
        return rawText.Trim();
    }

    private async Task<(string base64, string mimeType)> FetchImageDataAsync(string imageUrl)
    {
        // 1. Check if it's a local file path
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

        // 2. Check if local uploads folder has the file directly (bypass HTTP loopback)
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

        // 3. Otherwise fetch via HTTP with status validation
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

    private static string LoadVisionTemplate(string accessoryType = "fashion accessory lace / border / trim")
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "vision_feature_template.md");
        if (!File.Exists(templatePath))
        {
            templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "vision_feature_template.md");
        }

        string templateContent = File.Exists(templatePath)
            ? File.ReadAllText(templatePath)
            : SharedVisionPromptText;

        return templateContent.Replace("{{ACCESSORY_TYPE}}", accessoryType);
    }
}