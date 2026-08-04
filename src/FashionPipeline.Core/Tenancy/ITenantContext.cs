namespace FashionPipeline.Core.Tenancy;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsResolved { get; }
}