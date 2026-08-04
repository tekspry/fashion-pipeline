using System.Security.Cryptography;

namespace FashionPipeline.Core.Services;

public interface IImageHashService
{
    Task<string> ComputeSha256FromUrlAsync(string imageUrl, CancellationToken cancellationToken = default);
}

public class ImageHashService : IImageHashService
{
    private readonly HttpClient _httpClient;

    public ImageHashService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<string> ComputeSha256FromUrlAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        await using var stream = await _httpClient.GetStreamAsync(imageUrl, cancellationToken);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}