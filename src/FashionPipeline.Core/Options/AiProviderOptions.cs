namespace FashionPipeline.Core.Options;

public class AiProviderOptions
{
    public const string SectionName = "AIProvider";

    public string Provider { get; set; } = "Google"; // "Azure" or "Google"
    public AzureOptions Azure { get; set; } = new();
    public GoogleOptions Google { get; set; } = new();
}

public class AzureOptions
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Base Azure AI Foundry project endpoint.
    /// Example: https://azure-ai-foundry-ai-102.services.ai.azure.com
    /// Both GPT-5.6-sol (/openai/v1/chat/completions) and FLUX.2-pro
    /// (/providers/blackforestlabs/v1/flux-2-pro) are derived from this.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    public string VisionDeployment { get; set; } = "gpt-5.6-sol";
    public string PromptDeployment { get; set; } = "gpt-5.6-sol";
    public string ImageDeployment { get; set; } = "FLUX.2-pro";

    /// <summary>Image width in pixels for FLUX.2-pro (9:16 portrait = 1024).</summary>
    public int ImageWidth { get; set; } = 1024;

    /// <summary>Image height in pixels for FLUX.2-pro (9:16 portrait = 1792).</summary>
    public int ImageHeight { get; set; } = 1792;

    /// <summary>
    /// Optional override: if set, FLUX uses this endpoint instead of deriving from base Endpoint.
    /// Leave empty to auto-derive as {Endpoint}/providers/blackforestlabs/v1/flux-2-pro?api-version=preview
    /// </summary>
    public string FluxEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Optional override: if set, FLUX uses this API key instead of the shared ApiKey.
    /// Leave empty to use the shared ApiKey.
    /// </summary>
    public string FluxApiKey { get; set; } = string.Empty;
}

public class GoogleOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string VisionEndpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent";
    public string ImagenEndpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";
}
