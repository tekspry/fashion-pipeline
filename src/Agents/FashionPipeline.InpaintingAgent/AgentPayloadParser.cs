using System.Text.Json;

namespace FashionPipeline.InpaintingAgent;

/// <summary>
/// Parses the JSON payload sent by the Orchestrator to the InpaintingAgent.
/// Expected format:
/// {
///   "accessoryImageUri": "file:///path/to/lace.jpg",  // original accessory photo
///   "baseModelImageUri": "file:///path/to/base.png"   // AI-generated base model image from Step 3
/// }
/// </summary>
internal static class AgentPayloadParser
{
    public sealed record InpaintingRequest(string AccessoryImageUri, string BaseModelImageUri);

    public static InpaintingRequest? ParseInpaintingRequest(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('{')) return null;

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        if (!root.TryGetProperty("accessoryImageUri", out var accessoryEl))
            return null;
        if (!root.TryGetProperty("baseModelImageUri", out var modelEl))
            return null;

        var accessoryUri = accessoryEl.GetString();
        var modelUri = modelEl.GetString();

        if (string.IsNullOrWhiteSpace(accessoryUri) || string.IsNullOrWhiteSpace(modelUri))
            return null;

        return new InpaintingRequest(accessoryUri, modelUri);
    }
}
