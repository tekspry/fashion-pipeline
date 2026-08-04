namespace FashionPipeline.Core.Services;

public interface ICatalogPublishService
{
    Task PublishAsync(
        Guid tenantId,
        Guid accessoryId,
        string assetType,
        string assetUri,
        string promptUsed,
        CancellationToken cancellationToken = default);
}