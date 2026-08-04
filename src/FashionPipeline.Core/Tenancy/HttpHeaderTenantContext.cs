using Microsoft.AspNetCore.Http;

namespace FashionPipeline.Core.Tenancy;

public class HttpHeaderTenantContext : ITenantContext
{
  private readonly IHttpContextAccessor _httpContextAccessor;

  public HttpHeaderTenantContext(IHttpContextAccessor httpContextAccessor) =>
      _httpContextAccessor = httpContextAccessor;

  public bool IsResolved =>
      _httpContextAccessor.HttpContext?.Request.Headers.TryGetValue("X-Tenant-Id", out var v) == true
      && Guid.TryParse(v, out _);

  public Guid TenantId
  {
      get
      {
          if (!IsResolved) throw new InvalidOperationException("Tenant not resolved.");
          return Guid.Parse(_httpContextAccessor.HttpContext!.Request.Headers["X-Tenant-Id"]!);
      }
  }
}