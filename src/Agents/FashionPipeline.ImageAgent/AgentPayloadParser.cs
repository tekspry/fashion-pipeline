using System.Text.Json;

namespace FashionPipeline.ImageAgent;

internal static class AgentPayloadParser
{
    public sealed record ImageRequest(string Prompt, string RawImageUri);

    public static ImageRequest? ParseImageRequest(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('{')) return null;

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        if (!root.TryGetProperty("prompt", out var promptEl))
            return null;

        var prompt = promptEl.GetString();
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        string? rawImageUri = null;
        if (root.TryGetProperty("rawImageUri", out var rawEl))
            rawImageUri = rawEl.GetString();
        else if (root.TryGetProperty("imageUrl", out var urlEl))
            rawImageUri = urlEl.GetString();

        if (string.IsNullOrWhiteSpace(rawImageUri)) return null;

        return new ImageRequest(prompt, rawImageUri);
    }
}