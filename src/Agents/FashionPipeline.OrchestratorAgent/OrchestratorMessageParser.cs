using System.Text.Json;
using System.Text.RegularExpressions;

namespace FashionPipeline.OrchestratorAgent;

public sealed record OrchestratorPayload(Guid AccessoryId, Guid TenantId, string ImageUrl);

public static class OrchestratorMessageParser
{
    private static readonly Regex LegacyPattern = new(
        @"ID:\s*(?<id>[0-9a-fA-F-]{36}).*TenantId:\s*(?<tenant>[0-9a-fA-F-]{36}).*ImageUrl:\s*(?<url>\S+)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static OrchestratorPayload Parse(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Orchestrator message is empty.");

        if (text.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var accessoryId = Guid.Parse(root.GetProperty("accessoryId").GetString()!);
            var tenantId = Guid.Parse(root.GetProperty("tenantId").GetString()!);
            var imageUrl = root.TryGetProperty("imageUrl", out var urlEl)
                ? urlEl.GetString() ?? string.Empty
                : string.Empty;

            return new OrchestratorPayload(accessoryId, tenantId, imageUrl);
        }

        var match = LegacyPattern.Match(text);
        if (!match.Success)
            throw new ArgumentException("Unrecognized orchestrator message format.");

        return new OrchestratorPayload(
            Guid.Parse(match.Groups["id"].Value),
            Guid.Parse(match.Groups["tenant"].Value),
            match.Groups["url"].Value);
    }
}