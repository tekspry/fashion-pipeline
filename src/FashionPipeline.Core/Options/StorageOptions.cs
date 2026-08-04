namespace FashionPipeline.Core.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";
    public string Provider { get; set; } = "R2"; // or AzureBlob
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "fashion-assets";
    public string? ServiceUrl { get; set; } // R2: https://<account>.r2.cloudflarestorage.com
}