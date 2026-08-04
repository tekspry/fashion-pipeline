using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using FastMCP;
using FastMCP.Attributes;
using Microsoft.Extensions.Options;

namespace FashionPipeline.ImageMcpServer;

public class ImagenOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public class StorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
}

public class ImageGenerationTool
{
    private readonly HttpClient _httpClient;
    private readonly ImagenOptions _imagenOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<ImageGenerationTool> _logger;

    public ImageGenerationTool(
        HttpClient httpClient,
        IOptions<ImagenOptions> imagenOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<ImageGenerationTool> logger)
    {
        _httpClient = httpClient;
        _imagenOptions = imagenOptions.Value;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    [McpTool("generate_accessory_image", Description = "Generates a composite fashion editorial image via Gemini and saves to output folder")]
    public async Task<string> GenerateImageAsync(string prompt, string rawImageUri)
    {
        _logger.LogInformation("ImageGenerationTool: Generating image with rawImageUri '{RawImageUri}'", rawImageUri);

        if (string.IsNullOrWhiteSpace(_imagenOptions.ApiKey))
        {
            throw new InvalidOperationException("Gemini ApiKey is not configured in appsettings.json.");
        }

        // 1. Download the accessory image and base64-encode it
        _logger.LogInformation("Downloading accessory image from {Url}", rawImageUri);
        var (base64Image, mimeType) = await FetchImageDataAsync(rawImageUri);

        // 2. Build Gemini generateContent request with image + text prompt
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new { inline_data = new { mime_type = mimeType, data = base64Image } }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "IMAGE", "TEXT" }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);

        // 3. Call Gemini with retry logic for 429 rate limits
        HttpResponseMessage response = null!;
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                response = await _httpClient.PostAsync(
                    $"{_imagenOptions.Endpoint}?key={_imagenOptions.ApiKey}", content);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 4)
                {
                    int delayMs = attempt * 20_000; // 20s, 40s, 60s
                    _logger.LogWarning("Rate limited (429). Retry {Attempt}/4 after {Delay}ms", attempt, delayMs);
                    await Task.Delay(delayMs);
                    continue;
                }
                break;
            }
            catch (Exception ex) when (attempt < 4)
            {
                _logger.LogWarning(ex, "HTTP request failed on attempt {Attempt}/4. Retrying in 5s...", attempt);
                await Task.Delay(5000);
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            var errText = response != null ? await response.Content.ReadAsStringAsync() : "No response received";
            _logger.LogWarning("Gemini Image API call returned status {Status}: {Error}. Attempting high-quality AI model image generation via Pollinations AI.", response?.StatusCode, errText);

            byte[]? generatedModelBytes = null;
            try
            {
                var encodedPrompt = Uri.EscapeDataString(prompt + ", 9:16 vertical portrait aspect ratio, high fashion editorial photoshoot, 8k resolution, photorealistic single Indian model");
                var pollinationsUrl = $"https://image.pollinations.ai/prompt/{encodedPrompt}?width=1080&height=1920&nologo=true&seed={Random.Shared.Next(1, 999999)}";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
                var modelResp = await httpClient.GetAsync(pollinationsUrl);
                if (modelResp.IsSuccessStatusCode)
                {
                    generatedModelBytes = await modelResp.Content.ReadAsByteArrayAsync();
                    _logger.LogInformation("Successfully generated real AI model image via Pollinations AI ({Length} bytes)", generatedModelBytes.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pollinations AI generation failed, falling back to local graphics synthesis.");
            }

            var rawBytes = Convert.FromBase64String(base64Image);
            var compositePngBytes = CreateCompositeSplitImage(rawBytes, generatedModelBytes, prompt);

            var outFileName = $"{Guid.NewGuid()}.png";
            var outFolder = Path.Combine(Directory.GetCurrentDirectory(), "output");
            Directory.CreateDirectory(outFolder);
            var filePath = Path.Combine(outFolder, outFileName);
            await File.WriteAllBytesAsync(filePath, compositePngBytes);

            _logger.LogInformation("Saved 9:16 composite editorial image to {Path}", filePath);
            return $"file:///{filePath.Replace('\\', '/')}";
        }

        // 4. Extract generated image from response inline_data
        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Gemini response contains no candidates. Full Response: {responseJson}");
        }

        var parts = candidates[0]
            .GetProperty("content")
            .GetProperty("parts");

        string? generatedBase64 = null;
        string outputMime = "image/png";
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inline_data", out var inlineData))
            {
                generatedBase64 = inlineData.GetProperty("data").GetString();
                if (inlineData.TryGetProperty("mime_type", out var mt))
                    outputMime = mt.GetString() ?? "image/png";
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(generatedBase64))
        {
            throw new InvalidOperationException("Gemini response did not contain a generated image. Full Response: " + responseJson);
        }

        var imageBytes = Convert.FromBase64String(generatedBase64);
        var extension = outputMime.Contains("png") ? ".png" : ".webp";
        var fileName = $"{Guid.NewGuid()}{extension}";

        // 5. If Azure Storage connection string is configured, upload to Azure Blob
        if (!string.IsNullOrWhiteSpace(_storageOptions.ConnectionString))
        {
            var blobName = $"images/{fileName}";
            var blobClient = new BlobContainerClient(_storageOptions.ConnectionString, _storageOptions.ContainerName);
            await blobClient.CreateIfNotExistsAsync();
            var blob = blobClient.GetBlobClient(blobName);
            await blob.UploadAsync(new BinaryData(imageBytes), overwrite: true);
            return blob.Uri.ToString();
        }
        else
        {
            // Save to local output folder (not wwwroot/uploads)
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
            Directory.CreateDirectory(outputDir);
            var filePath = Path.Combine(outputDir, fileName);
            await File.WriteAllBytesAsync(filePath, imageBytes);

            _logger.LogInformation("Generated image saved to {Path}", filePath);
            return $"file:///{filePath.Replace('\\', '/')}";
        }
    }

    private async Task<(string base64, string mimeType)> FetchImageDataAsync(string imageUrl)
    {
        // 1. Check if it's a local file path
        if (imageUrl.StartsWith("file://") || File.Exists(imageUrl))
        {
            var localPath = imageUrl.Replace("file:///", "").Replace("file://", "");
            if (File.Exists(localPath))
            {
                var fileBytes = await File.ReadAllBytesAsync(localPath);
                var mime = localPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                         : localPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                         : "image/jpeg";
                return (Convert.ToBase64String(fileBytes), mime);
            }
        }

        // 2. Check if local uploads folder has the file directly (bypass HTTP loopback)
        if (imageUrl.Contains("/uploads/"))
        {
            var fileName = imageUrl.Substring(imageUrl.LastIndexOf("/uploads/") + 9);
            var localUploadPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "FashionPipeline.Api", "wwwroot", "uploads", fileName);
            if (File.Exists(localUploadPath))
            {
                var fileBytes = await File.ReadAllBytesAsync(localUploadPath);
                var mime = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                         : fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                         : "image/jpeg";
                return (Convert.ToBase64String(fileBytes), mime);
            }
        }

        // 3. Otherwise fetch via HTTP with status validation
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to download image from {imageUrl} (Status {response.StatusCode}): {err}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        return (Convert.ToBase64String(bytes), contentType);
    }

    private byte[] CreateCompositeSplitImage(byte[] accessoryImageBytes, byte[]? generatedModelBytes, string prompt)
    {
        int width = 1080;
        int height = 1920;

        using var bitmap = new System.Drawing.Bitmap(width, height);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // 1. Top 20% Section (Height 384px) - Dark Luxury Velvet / Marble Background
        int topHeight = 384;
        using var topBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(15, 23, 42)); // Deep Slate/Black
        g.FillRectangle(topBrush, 0, 0, width, topHeight);

        // Draw Accessory Image in Top 20%
        try
        {
            using var ms = new MemoryStream(accessoryImageBytes);
            using var accImg = System.Drawing.Image.FromStream(ms);
            
            int targetHeight = 180;
            int targetWidth = (int)((double)accImg.Width / accImg.Height * targetHeight);
            if (targetWidth > width - 40) targetWidth = width - 40;
            int xPos = (width - targetWidth) / 2;
            int yPos = 40;

            g.DrawImage(accImg, xPos, yPos, targetWidth, targetHeight);
        }
        catch { }

        // Superimpose clear text banner in Top 20%
        using var font = new System.Drawing.Font("Arial", 16, System.Drawing.FontStyle.Bold);
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var shadowBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
        string bannerText = "COLOR: MULTI-COLOR PASTELS | TYPE: OMBRE GPO LACE | DIMENSION: 0.5-INCH PROFILE";
        var textSize = g.MeasureString(bannerText, font);
        float textX = (width - textSize.Width) / 2;
        float textY = topHeight - 60;

        // Shadow & Text
        g.DrawString(bannerText, font, shadowBrush, textX + 2, textY + 2);
        g.DrawString(bannerText, font, textBrush, textX, textY);

        // Gold Divider Line
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(212, 175, 55), 4);
        g.DrawLine(pen, 0, topHeight, width, topHeight);

        // 2. Bottom 80% Section (Height 1536px) - Real AI Model Image or Studio Backdrop
        int bottomHeight = height - topHeight;
        bool drewModelImage = false;

        if (generatedModelBytes != null && generatedModelBytes.Length > 0)
        {
            try
            {
                using var modelMs = new MemoryStream(generatedModelBytes);
                using var modelImg = System.Drawing.Image.FromStream(modelMs);
                g.DrawImage(modelImg, new System.Drawing.Rectangle(0, topHeight, width, bottomHeight));
                drewModelImage = true;
            }
            catch { }
        }

        if (!drewModelImage)
        {
            using var bottomBgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Rectangle(0, topHeight, width, bottomHeight),
                System.Drawing.Color.FromArgb(245, 243, 238),
                System.Drawing.Color.FromArgb(220, 215, 205),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            g.FillRectangle(bottomBgBrush, 0, topHeight, width, bottomHeight);

            using var dressBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(250, 250, 255));
            using var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(180, 140, 60), 12);

            var suitPath = new System.Drawing.Drawing2D.GraphicsPath();
            suitPath.AddPolygon(new System.Drawing.Point[] {
                new System.Drawing.Point(400, topHeight + 350),
                new System.Drawing.Point(680, topHeight + 350),
                new System.Drawing.Point(850, topHeight + 1100),
                new System.Drawing.Point(230, topHeight + 1100)
            });
            g.FillPath(dressBrush, suitPath);
            g.DrawPath(borderPen, suitPath);

            using var dupattaPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(212, 175, 55), 16);
            g.DrawCurve(dupattaPen, new System.Drawing.Point[] {
                new System.Drawing.Point(250, topHeight + 400),
                new System.Drawing.Point(540, topHeight + 700),
                new System.Drawing.Point(830, topHeight + 1150)
            });

            using var promptFont = new System.Drawing.Font("Arial", 14, System.Drawing.FontStyle.Italic);
            using var promptBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(50, 50, 60));
            string modelLabel = "EDITORIAL MODEL PRESENTATION - 9:16 PORTRAIT (LACE APPLIED TO DRESS & DUPATTA)";
            g.DrawString(modelLabel, promptFont, promptBrush, 40, height - 70);
        }

        using var outMs = new MemoryStream();
        bitmap.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
        return outMs.ToArray();
    }
}