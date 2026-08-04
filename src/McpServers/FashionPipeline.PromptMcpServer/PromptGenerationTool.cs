using System.Text;
using System.Text.Json;
using FastMCP;
using FastMCP.Attributes;
using Microsoft.Extensions.Options;

namespace FashionPipeline.PromptMcpServer;

public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string VisionEndpoint { get; set; } = string.Empty;
}

public class PromptGenerationTool
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<PromptGenerationTool> _logger;

    public PromptGenerationTool(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<PromptGenerationTool> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    [McpTool("generate_image_prompts", Description = "Generates 2 image prompts using Gemini based on features and uploaded image")]
    public async Task<IEnumerable<string>> GeneratePromptsAsync(string featureJson, string imageUrl)
    {
        try
        {
        var promptInstruction = $@"
you need to generate 2 prompts for a lace design applied on a 2 different types and color of Indian ladies suits and Dupatta with specified breadth of lace. actual Lace design will be applied through the uploaded image which needs to be applied on the Indian Ladies suit. CRUCIALLY Use the uploaded image of lace design to generate the prompts.

Add the following consideration in the generated prompt:
- generated image should be of the female model in some South Indian tourist destination along with close up of the lace design.
- Make sure the focus must be on the lace design instead of generation surroundings. 
- generate only prompt which is further used to generate the appropriate image
- Actual lace design will be provided through the uploaded image
- enforce that the exact lace design is captured as shared in uploaded image and keep focus at it. 
- No need to change the design and features for lace, keep it exactly same as available in uploaded image and keep focus at it.
- Crucially, generated image must be of 2 parts, first part show the actual close up of the lace design which covers 20% of the image 
- remaining 80% covers the model wearing Indian dress with uploaded lace design applied at it.
- Consider the Indian dress design a replica of the top Indian designers.
- make sure the color of the suit or saree goes well with color of lace mentioned in features section.
- Also make sure the generate the image with full body of model. Don't hide the face or cut the legs in generated image.
- Generated image must be in portrait mode with 9:16 aspect ratio. Also it must have single model image.
- Write the color, type and dimension of the Lace with in the generated image in the 20% section where close up of Lace is shown kept horizontally. Text must be clear.
- Also, change the background of the actual (close up) Lace design part of the image. add a background with which Lace design even look more beautiful.
- CRUCIALLY replicate exact design of the lace in close up and when applied on dress. No need to focus too much on generating the surroundings, primary focus need to be on lace design

Features of the lace: 
{featureJson}

CRITICAL: Return the response STRICTLY as a JSON array of strings containing exactly 2 prompts. E.g. [""prompt 1"", ""prompt 2""]";

        var (base64Data, mimeType) = await FetchImageDataAsync(imageUrl);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = promptInstruction },
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
                response = await _httpClient.PostAsync($"{_options.VisionEndpoint}?key={_options.ApiKey}", content);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 4)
                {
                    // Gemini free tier requires 15-20s cool-down per 429 response
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

        // Safe extraction: Check if candidates exist
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Gemini response contains no candidates. Full Response: {responseJson}");
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var contentEl) || !contentEl.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Gemini response candidate missing content parts. Full Response: {responseJson}");
        }

        // Find the part that actually contains text (handles reasoning/thinking parts in Gemini Flash)
        string responseText = string.Empty;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textProp))
            {
                var val = textProp.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    responseText = val;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new InvalidOperationException($"No text found in Gemini response parts. Full Response: {responseJson}");
        }

        // Strip markdown code fences if wrapped by Gemini
        responseText = responseText.Trim();
        if (responseText.StartsWith("```json"))
        {
            responseText = responseText.Substring(7);
        }
        else if (responseText.StartsWith("```"))
        {
            responseText = responseText.Substring(3);
        }
        if (responseText.EndsWith("```"))
        {
            responseText = responseText.Substring(0, responseText.Length - 3);
        }
        List<string> resultPrompts;
        try
        {
            resultPrompts = JsonSerializer.Deserialize<List<string>>(responseText) ?? new List<string> { responseText };
        }
        catch
        {
            resultPrompts = new List<string> { responseText };
        }
        return resultPrompts;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "PromptGenerationTool failed generating prompts for imageUrl: {ImageUrl}", imageUrl);
        throw;
    }
        



        // var responseJson = await response.Content.ReadAsStringAsync();
        // using var doc = JsonDocument.Parse(responseJson);
        // var responseText = doc.RootElement
        //     .GetProperty("candidates")[0]
        //     .GetProperty("content")
        //     .GetProperty("parts")[0]
        //     .GetProperty("text")
        //     .GetString() ?? "[]";

        // // Extract the JSON array from the response in case it's wrapped in markdown code blocks
        // responseText = responseText.Trim();
        // if (responseText.StartsWith("```json"))
        // {
        //     responseText = responseText.Substring(7);
        // }
        // if (responseText.EndsWith("```"))
        // {
        //     responseText = responseText.Substring(0, responseText.Length - 3);
        // }
        // responseText = responseText.Trim();

        // try
        // {
        //     return JsonSerializer.Deserialize<List<string>>(responseText) ?? new List<string>();
        // }
        // catch
        // {
        //     return new List<string> { responseText };
        // }
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