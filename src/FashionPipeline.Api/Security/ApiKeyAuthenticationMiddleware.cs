namespace FashionPipeline.Api.Security;

public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Use GetChildren() to enumerate actual configured keys.
        // This avoids the .Get<string[]>() binder quirk where an absent/empty
        // section can return a single-element array containing the section name.
        var validKeys = _configuration
            .GetSection("ApiKeys:ValidKeys")
            .GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        // No keys configured → open access (dev / no-auth mode)
        if (validKeys.Length == 0)
        {
            await _next(context);
            return;
        }

        // Always allow static files, Swagger, and Hangfire dashboard
        if (context.Request.Path.StartsWithSegments("/uploads") ||
            context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.StartsWithSegments("/hangfire"))
        {
            await _next(context);
            return;
        }

        // Enforce API key header
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
            !validKeys.Contains(provided.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(context);
    }
}