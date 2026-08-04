using System;

namespace FashionPipeline.Core.Entities;

public enum AccessoryStatus
{
    Pending, Processing, VideoPending, RequiresManualVideo, Complete, Failed
}

public class Accessory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } // SaaS Isolation
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RawImageUri { get; set; } = string.Empty;
    public string ImageHash { get; set; } = string.Empty; // For bypassing Gemini extraction
    public string? ExtractedFeatures { get; set; }
    public AccessoryStatus Status { get; set; } = AccessoryStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}