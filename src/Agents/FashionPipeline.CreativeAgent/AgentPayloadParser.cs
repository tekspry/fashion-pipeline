using System.Text.Json;

namespace FashionPipeline.CreativeAgent;

internal static class AgentPayloadParser
{
    public static string? GetFeatureJson(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.StartsWith('{')) return null;

        using var doc = JsonDocument.Parse(text);

        if (doc.RootElement.TryGetProperty("featureJson", out var wrapped))
        {
            return wrapped.ValueKind == JsonValueKind.String
                ? wrapped.GetString()
                : wrapped.GetRawText();
        }

        if (doc.RootElement.TryGetProperty("Color", out _) ||
            doc.RootElement.TryGetProperty("Type", out _))
        {
            return text;
        }

        return null;
    }

    public static string? GetImageUrl(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.StartsWith('{')) return null;

        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("imageUrl", out var val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }
        return null;
    }
}