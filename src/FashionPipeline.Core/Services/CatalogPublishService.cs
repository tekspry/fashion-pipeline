using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;

namespace FashionPipeline.Core.Services;

public sealed class CatalogPublishService : ICatalogPublishService
{
    private readonly AppDbContext _db;

    public CatalogPublishService(AppDbContext db) => _db = db;

    public async Task PublishAsync(
        Guid tenantId,
        Guid accessoryId,
        string assetType,
        string assetUri,
        string promptUsed,
        CancellationToken cancellationToken = default)
    {
        _db.CurrentTenantId = tenantId;

        _db.GeneratedAssets.Add(new GeneratedAsset
        {
            TenantId = tenantId,
            AccessoryId = accessoryId,
            AssetType = assetType,
            AssetUri = assetUri,
            PromptUsed = promptUsed,
            IsApproved = false
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}