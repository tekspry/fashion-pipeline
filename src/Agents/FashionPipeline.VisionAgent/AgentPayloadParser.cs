using System.Text.Json;

namespace FashionPipeline.VisionAgent;

internal static class AgentPayloadParser
{
    public static string? GetString(string text, string propertyName)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        if (text.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop))
                return prop.GetString();
            return null;
        }

        return propertyName.Equals("imageUrl", StringComparison.OrdinalIgnoreCase) ? text : null;
    }
}