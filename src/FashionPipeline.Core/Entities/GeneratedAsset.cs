using System;

namespace FashionPipeline.Core.Entities;

public class GeneratedAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } // SaaS Isolation
    public Guid AccessoryId { get; set; }
    public string AssetType { get; set; } = string.Empty; // "Image" or "Video"
    public string AssetUri { get; set; } = string.Empty;
    public string PromptUsed { get; set; } = string.Empty;
    public bool IsApproved { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}