using FashionPipeline.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionPipeline.Core.Data;

public class AppDbContext : DbContext
{
    public Guid CurrentTenantId { get; set; } // Set via ITenantContext

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Accessory> Accessories { get; set; } = null!;
    public DbSet<GeneratedAsset> GeneratedAssets { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Accessory>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.ImageHash).HasMaxLength(64);
             e.Property(x => x.Status).HasConversion<string>();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId); // SaaS Global Filter
        });
        
        modelBuilder.Entity<GeneratedAsset>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccessoryId, x.PromptUsed }); // Optimization for Cache Lookups
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId); // SaaS Global Filter
        });
    }
}