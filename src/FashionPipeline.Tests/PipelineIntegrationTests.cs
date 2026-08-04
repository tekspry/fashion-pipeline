using System;
using System.Threading.Tasks;
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;
using FashionPipeline.Core.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FashionPipeline.Tests.Integration;

public class PipelineIntegrationTests
{
    [Fact(Skip = "Step 1 / Phase C: re-enable after A2A orchestrator wiring replaces NotImplementedException.")]
    public async Task Job_Changes_Status_To_Complete_On_Success()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new AppDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var accessory = new Accessory { Id = Guid.NewGuid(), RawImageUri = "https://mock.com/img.jpg" };
        dbContext.Accessories.Add(accessory);
        await dbContext.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddHttpClient();
        services.Configure<AgentOptions>(_ => { });
        var provider = services.BuildServiceProvider();

        var job = new PipelineAgentJob(
            dbContext,
            provider.GetRequiredService<IOptions<AgentOptions>>(),
            provider.GetRequiredService<IHttpClientFactory>());

        await job.ExecuteAsync(accessory.Id, tenantId: Guid.NewGuid());

        var updatedAccessory = await dbContext.Accessories.FindAsync(accessory.Id);
        Assert.Equal(AccessoryStatus.Complete, updatedAccessory!.Status);
    }
}
