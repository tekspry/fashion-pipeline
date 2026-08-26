using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FastMCP;
using FastMCP.Attributes;
using Microsoft.Extensions.Options;
using FashionPipeline.Core.Options;

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
    private readonly AiProviderOptions _providerOptions;
    private readonly ILogger<PromptGenerationTool> _logger;

    public PromptGenerationTool(
        HttpClient httpClient, 
        IOptions<GeminiOptions> options, 
        IOptions<AiProviderOptions> providerOptions,
        ILogger<PromptGenerationTool> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _providerOptions = providerOptions.Value;
        _logger = logger;
    }

    [McpTool("generate_image_prompts", Description = "Generates 2 image prompts using Gemini or Azure OpenAI based on features and uploaded image")]
    public async Task<IEnumerable<string>> GeneratePromptsAsync(string featureJson, string imageUrl)
    {
        var isAzure = string.Equals(_providerOptions.Provider, "Azure", StringComparison.OrdinalIgnoreCase);

        if (isAzure)
        {
            if (string.IsNullOrWhiteSpace(_providerOptions.Azure?.ApiKey) || string.IsNullOrWhiteSpace(_providerOptions.Azure?.Endpoint))
            {
                throw new InvalidOperationException("Provider is configured as 'Azure', but Azure ApiKey or Endpoint is missing in appsettings.");
            }
            return await GeneratePromptsViaAzureAsync(featureJson, imageUrl);
        }

        return await GeneratePromptsViaGoogleAsync(featureJson, imageUrl);
    }

    private async Task<IEnumerable<string>> GeneratePromptsViaAzureAsync(string featureJson, string imageUrl)
    {
        // --- Guardrail: validate inputs ---
        if (string.IsNullOrWhiteSpace(featureJson))
            throw new ArgumentException("featureJson must not be empty for prompt generation.", nameof(featureJson));
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new ArgumentException("imageUrl must not be empty for prompt generation.", nameof(imageUrl));

        var azureOpt = _providerOptions.Azure;
        var model = !string.IsNullOrEmpty(azureOpt.PromptDeployment) ? azureOpt.PromptDeployment : "gpt-5.6-sol";

        _logger.LogInformation("PromptMcp: Generating prompts via Azure ({Model}) for {ImageUrl}", model, imageUrl);
        _logger.LogInformation("⚠️  COST ALERT: This call uses {Model} (gpt-5.6-sol ≈ ₹0.50/run). Making exactly 1 API call.", model);

        var (base64Data, mimeType) = await FetchImageDataAsync(imageUrl);
        var dataUri = $"data:{mimeType};base64,{base64Data}";

        // --- Guardrail: cap base64 payload ---
        if (base64Data.Length > 5_000_000)
            throw new InvalidOperationException($"Image payload exceeds 5MB base64 limit ({base64Data.Length} chars). Resize the image before processing.");

        var formattedFeatures = FormatFeatureContext(featureJson);
        var promptInstruction = LoadCreativeTemplate(formattedFeatures);

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
                        new { type = "text", text = promptInstruction },
                        new { type = "image_url", image_url = new { url = dataUri } }
                    }
                }
            },
            max_completion_tokens = 2048,
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
            throw new HttpRequestException($"Azure {model} Prompt API error (Status {response.StatusCode}): {respText}");
        }

        using var doc = JsonDocument.Parse(respText);
        var rawText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return ParsePromptsFromText(rawText, featureJson);
    }

    private async Task<IEnumerable<string>> GeneratePromptsViaGoogleAsync(string featureJson, string imageUrl)
    {
        try
        {
        var formattedFeatures = FormatFeatureContext(featureJson);
        var promptInstruction = LoadCreativeTemplate(formattedFeatures);

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

        return ParsePromptsFromText(responseText, featureJson);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "PromptGenerationTool failed generating prompts for imageUrl: {ImageUrl}", imageUrl);
        throw;
    }
    }

    private static IEnumerable<string> ParsePromptsFromText(string responseText, string featureJson)
    {
        var cleaned = responseText.Trim();
        if (cleaned.StartsWith("```json"))
            cleaned = cleaned.Substring(7);
        else if (cleaned.StartsWith("```"))
            cleaned = cleaned.Substring(3);
        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3);

        cleaned = cleaned.Trim();

        try
        {
            if (cleaned.StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<string>>(cleaned);
                if (list != null && list.Count > 0 && !list[0].Contains("\"error\"")) return list;
            }
            else if (cleaned.StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(cleaned);
                if (doc.RootElement.TryGetProperty("error", out var errProp))
                {
                    throw new InvalidOperationException($"Prompt generation API returned an error response: {cleaned}");
                }
                if (doc.RootElement.TryGetProperty("prompts", out var promptsArr) && promptsArr.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var item in promptsArr.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            list.Add(item.GetString()!);
                    }
                    if (list.Count > 0) return list;
                }
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Json parsing failed
        }

        // Fail-fast guardrail: if cleaned string looks like JSON or contains error text, throw rather than returning as prompt
        if (cleaned.StartsWith("{") || cleaned.StartsWith("[") || cleaned.Contains("\"error\":"))
        {
            throw new InvalidOperationException($"Failed to parse valid prompts from AI response. Raw output: {responseText}");
        }

        return new List<string> { cleaned };
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

    private static string FormatFeatureContext(string featureJson)
    {
        if (string.IsNullOrWhiteSpace(featureJson)) return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(featureJson);
            var root = doc.RootElement;

            var sb = new StringBuilder();

            // 1. Title / Lead Introduction
            if (root.TryGetProperty("Title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine(titleProp.GetString()!);
                sb.AppendLine();
            }

            // 2. Precise Design Features
            sb.AppendLine("Precise Design Features:");
            if (root.TryGetProperty("PreciseDesignFeatures", out var pdf) && pdf.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in pdf.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        sb.AppendLine($"- {prop.Name}: {prop.Value.GetString()}");
                }
            }
            else if (root.TryGetProperty("Features", out var featProp) && featProp.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine($"- {featProp.GetString()}");
            }

            sb.AppendLine();
            sb.AppendLine("Color & Finish:");

            // 3. Color Identification
            if (root.TryGetProperty("ColorIdentification", out var ci) && ci.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ci.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        sb.AppendLine($"- {prop.Name}: {prop.Value.GetString()}");
                }
            }
            else
            {
                if (root.TryGetProperty("Color", out var cProp)) sb.AppendLine($"- Color: {cProp.GetString()}");
                if (root.TryGetProperty("Material", out var mProp)) sb.AppendLine($"- Material: {mProp.GetString()}");
            }

            if (root.TryGetProperty("Type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine($"- Dimension / Type: {typeProp.GetString()}");
            }

            sb.AppendLine();
            sb.AppendLine("Suggested Applications:");

            // 4. Suggested Applications
            if (root.TryGetProperty("SuggestedApplications", out var sa) && sa.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in sa.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        sb.AppendLine($"- {item.GetString()}");
                }
            }
            else if (root.TryGetProperty("Style", out var styleProp) && styleProp.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine($"- {styleProp.GetString()}");
            }

            return sb.ToString();
        }
        catch
        {
            return featureJson;
        }
    }

    private static string LoadCreativeTemplate(
        string featureContext, 
        string accessoryType = "fashion accessory (button/lace)", 
        string location = "a modern luxury high-rise corner office or upscale architectural workspace in India looking like a boss", 
        int promptCount = 1)
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "creative_prompt_template.md");
        if (!File.Exists(templatePath))
        {
            templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "creative_prompt_template.md");
        }

        string templateContent = File.Exists(templatePath)
            ? File.ReadAllText(templatePath)
            : DefaultCreativePromptTemplate;

        return templateContent
            .Replace("{{PROMPT_COUNT}}", promptCount.ToString())
            .Replace("{{ACCESSORY_TYPE}}", accessoryType)
            .Replace("{{LOCATION}}", location)
            .Replace("{{FEATURE_CONTEXT}}", featureContext);
    }

    private const string DefaultCreativePromptTemplate = @"you need to generate {{PROMPT_COUNT}} prompt for a {{ACCESSORY_TYPE}} design applied on Indian ladies Kurti and suits of Indian ladies dress with specified {{ACCESSORY_TYPE}}. The actual {{ACCESSORY_TYPE}} design will be applied through the uploaded reference image which needs to be applied on the Indian Ladies suit. CRUCIALLY Use the uploaded image of {{ACCESSORY_TYPE}} design to generate the prompts.

Add the following considerations in each generated prompt:
- The generated image must be a high-definition, portrait-oriented (9:16 aspect ratio) split-image fashion product and editorial photograph, divided horizontally into two distinct sections, engineered to lock primary focus strictly onto the {{ACCESSORY_TYPE}} design.
- Top Part (Exactly 20% of Image): A sharp, high-resolution macro close-up photograph of the exact {{ACCESSORY_TYPE}} from the reference image, laid out horizontally across the frame. The camera focus is locked deeply onto its distinct sculptural details, material texture, silhouette, and high-luster finish. The plain background is completely replaced with an aesthetically complementary luxury surface (such as dark polished emerald quartzite stone with delicate golden veins, dark textured slate, or rich polished marble under direct studio lighting) that makes the {{ACCESSORY_TYPE}} pop with beauty. Clearly legible, crisp horizontal white text is overlaid across this section stating the exact: ""COLOR: [Color] | TYPE: [Type/Silhouette] | DIMENSION: [Dimension]"".
- Bottom Part (Exactly 80% of Image): A sharp, full-body portrait photograph of a single Indian female model / executive leader standing with commanding, confident ""boss"" posture inside {{LOCATION}}. The background is rendered in soft-focus bokeh to prevent any visual distraction. The model wears a top designer-replica contemporary Indian dress / executive-ethnic suit (such as a tailored straight-cut long kurti with stand collar and matching pants, or a luxury anarkali / raw silk suit) in a solid, elegant color that pairs harmoniously with the color of the {{ACCESSORY_TYPE}}.
- CRUCIALLY, the exact {{ACCESSORY_TYPE}} from the uploaded reference image must be applied with absolute fidelity and replicated onto the garment (e.g., in a vertical series of centerpiece buttons along the front center neck placket of the kurti and on sleeve cuffs, or along the neckline and borders). Primary focus remains sharply locked onto the applied {{ACCESSORY_TYPE}} ornaments.
- Full-body view: The single model stands tall facing forward with an empowered, poised expression; her face is completely clear and unhidden, and her full-body silhouette, full legs, and footwear (such as designer heels or sleek juttis) are entirely captured within the frame without being cropped.
- Do NOT generate multiple models. Single model only.

Features of the {{ACCESSORY_TYPE}}: 
{{FEATURE_CONTEXT}}

CRITICAL: Return the response STRICTLY as a JSON object containing a ""prompts"" array of strings with exactly {{PROMPT_COUNT}} prompt(s). E.g. {{ ""prompts"": [""Deep Seafoam Green Raw Silk Executive Suit with Textured Champagne Gold Starfish Buttons\nA high-definition, portrait-oriented (9:16 aspect ratio) split-image fashion product and editorial photograph, divided horizontally into two distinct sections...""] }}";
}