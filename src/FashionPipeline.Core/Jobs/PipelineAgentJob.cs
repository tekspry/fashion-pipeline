using System.Text.Json;
using A2A;
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;
using FashionPipeline.Core.Options;
using Microsoft.Extensions.Options;

namespace FashionPipeline.Core.Jobs;

public class AgentOptions
{
    public const string SectionName = "Agents";
    public string OrchestratorUrl { get; set; } = "http://localhost:5050";
    public string VisionUrl { get; set; } = "http://localhost:5101";
    public string CreativeUrl { get; set; } = "http://localhost:5201";
    public string ImageUrl { get; set; } = "http://localhost:5301";
    public string VideoUrl { get; set; } = "http://localhost:5401";
    public string InpaintingUrl { get; set; } = "http://localhost:5501";
}

public class PipelineAgentJob
{
    private readonly AppDbContext _dbContext;
    private readonly AgentOptions _agentOptions;
    private readonly IHttpClientFactory _httpClientFactory;

    public PipelineAgentJob(
        AppDbContext dbContext,
        IOptions<AgentOptions> agentOptions,
        IHttpClientFactory httpClientFactory)
    {
        _dbContext = dbContext;
        _agentOptions = agentOptions.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task ExecuteAsync(Guid accessoryId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        _dbContext.CurrentTenantId = tenantId;
        var accessory = await _dbContext.Accessories.FindAsync(new object[] { accessoryId }, cancellationToken);
        if (accessory is null) return;

        try
        {
            accessory.Status = AccessoryStatus.Processing;
            await _dbContext.SaveChangesAsync(cancellationToken);
            var httpClient = _httpClientFactory.CreateClient("PipelineAgent");
            var orchestratorUri = new Uri(_agentOptions.OrchestratorUrl.TrimEnd('/') + "/");
            var a2aClient = new A2AClient(orchestratorUri, httpClient);
            var payload = JsonSerializer.Serialize(new
            {
                accessoryId = accessory.Id,
                tenantId,
                imageUrl = accessory.RawImageUri
            });
            _ = await a2aClient.SendMessageAsync(new SendMessageRequest
            {
                Message = new Message
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = Role.User,
                    Parts = [Part.FromText(payload)]
                }
            }, cancellationToken);
            await _dbContext.Entry(accessory).ReloadAsync(cancellationToken);
            if (accessory.Status != AccessoryStatus.Complete &&
                accessory.Status != AccessoryStatus.RequiresManualVideo)
            {
                accessory.Status = AccessoryStatus.Complete;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch
        {
            accessory.Status = AccessoryStatus.Failed;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}