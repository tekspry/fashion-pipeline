using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FastMCP;
using FastMCP.Attributes;
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
    private readonly ILogger<FeatureExtractionTool> _logger;

    public FeatureExtractionTool(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<FeatureExtractionTool> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    [McpTool("extract_accessory_features", Description = "Calls Gemini Vision to extract JSON features from an image URL")]
    public async Task<string> ExtractFeaturesAsync(string imageUrl)
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
                        new { text = "now identify the exact color and describe precise design features for this newly uploaded Organza multi colour lace image generally applied on white color ladies suits and dupattas. The actual size of lace is 1 inch broad. Return ONLY a JSON object with keys like: Color, Type, Material, Vibe, Style, Features." },
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

        // Strip markdown code fences that Gemini sometimes wraps responses in
        rawText = rawText.Trim();
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
}