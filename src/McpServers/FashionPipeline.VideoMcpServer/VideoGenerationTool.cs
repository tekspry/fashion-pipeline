using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using FastMCP;
using FastMCP.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FashionPipeline.VideoMcpServer;

public class KlingOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public class StorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
}

public class VideoGenerationTool
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly KlingOptions _klingOptions;
    private readonly StorageOptions _storageOptions;
    private readonly VideoApiOptions _videoApiOptions;

    public VideoGenerationTool(HttpClient httpClient, IMemoryCache cache,
        IOptions<KlingOptions> klingOptions, IOptions<StorageOptions> storageOptions,
        IOptions<VideoApiOptions> videoApiOptions)
    {
        _httpClient = httpClient;
        _cache = cache;
        _klingOptions = klingOptions.Value;
        _storageOptions = storageOptions.Value;
        _videoApiOptions = videoApiOptions.Value;
    }

    [McpTool("generate_accessory_video", Description = "Generates a 5-second promotional video from an image URL via Kling AI")]
    public async Task<string> GenerateVideoAsync(string imageUrl)
    {
        if (!_videoApiOptions.QuotaAvailable)
        {
            return "QUOTA_EXHAUSTED";
        }

        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _klingOptions.ApiKey);

        // 1. Kick off the async video generation job
        var requestBody = new
        {
            model_name = "kling-v1",
            image = imageUrl,
            prompt = "generate a short video for uploaded image. Make sure not to make any change in the image just add animation. Also the image is divided in 2 parts, top part shows the close up of lace design, no need to animate the this lace design. Animate only the bottom part of the image when model is wearing a dress on which this lace design is applied. CRICUALLY keep the top section and text written should not change or animated keep them as shown in the image",
            duration = "5",
            aspect_ratio = "9:16"
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_klingOptions.Endpoint, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var taskId = doc.RootElement.GetProperty("data").GetProperty("task_id").GetString()!;

        // 2. Poll for completion (Kling AI is asynchronous)
        string? videoUrl = null;
        for (int i = 0; i < 30; i++) // Poll for up to 5 minutes
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            var statusResponse = await _httpClient.GetAsync($"{_klingOptions.Endpoint}/{taskId}");
            var statusJson = await statusResponse.Content.ReadAsStringAsync();
            using var statusDoc = JsonDocument.Parse(statusJson);
            var status = statusDoc.RootElement.GetProperty("data").GetProperty("task_status").GetString();
            if (status == "succeed")
            {
                videoUrl = statusDoc.RootElement.GetProperty("data").GetProperty("task_result")
                    .GetProperty("videos")[0].GetProperty("url").GetString();
                break;
            }
            if (status == "failed") throw new Exception("Kling video generation failed.");
        }

        if (videoUrl == null) throw new TimeoutException("Kling video generation timed out.");

        // 3. Download and save to Azure Blob Storage
        var videoBytes = await _httpClient.GetByteArrayAsync(videoUrl);
        var blobName = $"videos/{Guid.NewGuid()}.mp4";
        var blobClient = new BlobContainerClient(_storageOptions.ConnectionString, _storageOptions.ContainerName);
        await blobClient.CreateIfNotExistsAsync();
        var blob = blobClient.GetBlobClient(blobName);
        await blob.UploadAsync(new BinaryData(videoBytes), overwrite: true);

        return blob.Uri.ToString();
    }
}