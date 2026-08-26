using FashionPipeline.Api.Security;
using FashionPipeline.Core.Data;
using FashionPipeline.Core.Entities;
using FashionPipeline.Core.Jobs;
using FashionPipeline.Core.Options;
using FashionPipeline.Core.Services;
using FashionPipeline.Core.Tenancy;
using Hangfire;
using Hangfire.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<PromptOptions>(builder.Configuration.GetSection(PromptOptions.SectionName));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpHeaderTenantContext>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHangfire(config => config
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(builder.Configuration.GetConnectionString("DefaultConnection")));
// Disable automatic retries — failed pipeline jobs should not auto-re-run on restart
GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 0 });
// Limit workers to 2: pipeline jobs are long-running & SQLite is single-writer; 20 workers cause lock contention
builder.Services.AddHangfireServer(options => { options.WorkerCount = 2; });

builder.Services.AddHttpClient<IImageHashService, ImageHashService>();

//builder.Services.AddHttpClient<PipelineAgentJob>()
  //  .AddStandardResilienceHandler(); // Polly via Microsoft.Extensions.Http.Resilience (Step 12)

builder.Services.AddHttpClient("PipelineAgent", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var orchestratorUrl = config[$"{AgentOptions.SectionName}:OrchestratorUrl"] ?? "http://localhost:5050";
    client.BaseAddress = new Uri(orchestratorUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(15); // No Polly - pipeline is fire-and-forget via Hangfire
});

builder.Services.AddTransient<PipelineAgentJob>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000;");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseHangfireDashboard("/hangfire");
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

//Client must send
//X-Tenant-Id: <guid>
//X-Api-Key: <key> (when configured)
app.MapPost("/api/v1/accessory/process", async (
    ProcessAccessoryRequest request,
    [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
    AppDbContext db,
    //ITenantContext tenantContext,
    IImageHashService imageHashService,
    IBackgroundJobClient jobClient,
    CancellationToken cancellationToken) =>
{
    db.CurrentTenantId = tenantId;

    var imageHash = string.IsNullOrWhiteSpace(request.ImageUrl)
        ? string.Empty
        : await imageHashService.ComputeSha256FromUrlAsync(request.ImageUrl, cancellationToken);

    var accessory = new Accessory
    {
        TenantId = tenantId,
        Name = request.Name,
        Category = request.Category,
        RawImageUri = request.ImageUrl,
        ImageHash = imageHash,
        Status = AccessoryStatus.Pending
    };

    db.Accessories.Add(accessory);
    await db.SaveChangesAsync(cancellationToken);

    // Disabled auto-enqueue to prevent duplicate parallel pipeline runs.
    // var jobId = jobClient.Enqueue<PipelineAgentJob>(job =>
    //     job.ExecuteAsync(accessory.Id, tenantId, CancellationToken.None));
    string? jobId = null;

    return Results.Accepted($"/api/v1/accessory/{accessory.Id}", new { jobId, accessoryId = accessory.Id });
})
.WithName("ProcessAccessory")
.WithOpenApi();

app.MapPost("/api/v1/accessory/upload", async (
    IFormFile file,
    [FromForm] string name,
    [FromForm] string category,
    [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
    AppDbContext db,
    IImageHashService imageHashService,
    IBackgroundJobClient jobClient,
    IWebHostEnvironment env,
    CancellationToken ct) =>
{
    var uploadPath = env.WebRootPath != null
        ? Path.Combine(env.WebRootPath, "uploads")
        : Path.Combine(env.ContentRootPath, "wwwroot", "uploads");
    Directory.CreateDirectory(uploadPath);

    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
    var filePath = Path.Combine(uploadPath, fileName);
    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream, ct);
    }

    var localUrl = $"http://localhost:5000/uploads/{fileName}";

    db.CurrentTenantId = tenantId;
    //var imageHash = await imageHashService.ComputeSha256FromUrlAsync(localUrl, ct);
    string imageHash;
    using (var hashStream = File.OpenRead(filePath))
    {
        var hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(hashStream, ct);
        imageHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
    }   

    var accessory = new Accessory
    {
        TenantId = tenantId,
        Name = name,
        Category = category,
        RawImageUri = localUrl,
        ImageHash = imageHash,
        Status = AccessoryStatus.Pending
    };

    db.Accessories.Add(accessory);
    await db.SaveChangesAsync(ct);

    // Disabled auto-enqueue to prevent duplicate parallel pipeline runs and extra model costs.
    // The pipeline will be triggered explicitly on demand.
    // var jobId = jobClient.Enqueue<PipelineAgentJob>(job =>
    //     job.ExecuteAsync(accessory.Id, tenantId, CancellationToken.None));
    string? jobId = null;

    return Results.Accepted($"/api/v1/accessory/{accessory.Id}", new { jobId, accessoryId = accessory.Id, localUrl });
})
.WithName("UploadAccessory")
.WithOpenApi()
.DisableAntiforgery();

app.MapGet("/api/v1/accessory/{id:guid}", async (Guid id, AppDbContext db, ITenantContext tenantContext, CancellationToken ct) =>
{
    db.CurrentTenantId = tenantContext.TenantId;
    var accessory = await db.Accessories.FindAsync(new object[] { id }, ct);
    return accessory is null ? Results.NotFound() : Results.Ok(accessory);
});

app.MapGet("/api/v1/accessory/{id:guid}/assets", async (Guid id, AppDbContext db, ITenantContext tenantContext, CancellationToken ct) =>
{
    db.CurrentTenantId = tenantContext.TenantId;
    var assets = await db.GeneratedAssets
        .Where(a => a.AccessoryId == id)
        .OrderByDescending(a => a.CreatedAt)
        .ToListAsync(ct);
    return Results.Ok(assets);
});

app.MapPatch("/api/v1/assets/{assetId:guid}/approval", async (
    Guid assetId,
    ApprovalRequest body,
    AppDbContext db,
    ITenantContext tenantContext,
    CancellationToken ct) =>
{
    db.CurrentTenantId = tenantContext.TenantId;
    var asset = await db.GeneratedAssets.FindAsync(new object[] { assetId }, ct);
    if (asset is null) return Results.NotFound();
    asset.IsApproved = body.IsApproved;
    await db.SaveChangesAsync(ct);
    return Results.Ok(asset);
});

app.MapPost("/api/v1/accessory/{id:guid}/run", async (
    Guid id,
    [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
    AppDbContext db,
    IBackgroundJobClient jobClient,
    CancellationToken ct) =>
{
    db.CurrentTenantId = tenantId;
    var accessory = await db.Accessories.FindAsync(new object[] { id }, ct);
    if (accessory is null) return Results.NotFound();
    var jobId = jobClient.Enqueue<PipelineAgentJob>(job =>
        job.ExecuteAsync(accessory.Id, tenantId, CancellationToken.None));
    return Results.Accepted($"/api/v1/accessory/{id}", new { jobId, accessoryId = id });
})
.WithName("RunAccessoryPipeline")
.WithOpenApi();

app.MapGet("/api/v1/accessory/manual-video", async (AppDbContext db, ITenantContext tenantContext, CancellationToken ct) =>
{
    db.CurrentTenantId = tenantContext.TenantId;
    var items = await db.Accessories
        .Where(a => a.Status == AccessoryStatus.RequiresManualVideo)
        .ToListAsync(ct);
    return Results.Ok(items);
});

app.Run();

public record ApprovalRequest(bool IsApproved);

public record ProcessAccessoryRequest(string Name, string Category, string ImageUrl);