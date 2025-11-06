using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using WeatherImageFunction.Models;

namespace WeatherImageFunction.Services;

public class ImageService : IImageService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ImageService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IBlobStorageService _blobStorageService;

    public ImageService(
        HttpClient httpClient,
        ILogger<ImageService> logger,
        IConfiguration configuration,
        IBlobStorageService blobStorageService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
        _blobStorageService = blobStorageService;
    }

    public async Task<byte[]> GetImageAsync(string searchKeyword)
    {
        try
        {
            var unsplashAccessKey = _configuration["UnsplashAccessKey"];
            if (string.IsNullOrEmpty(unsplashAccessKey))
                throw new InvalidOperationException("Unsplash Access Key is not configured");

            var url = $"https://api.unsplash.com/photos/random?query={Uri.EscapeDataString(searchKeyword)}";

            // Build request with official headers instead of client_id query
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Add("Accept-Version", "v1");
            req.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", unsplashAccessKey);

            var metaResponse = await _httpClient.SendAsync(req);
            if (metaResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                LogUnsplashRate(metaResponse);
                metaResponse.EnsureSuccessStatusCode(); // will throw 403 with context
            }
            metaResponse.EnsureSuccessStatusCode();

            var content = await metaResponse.Content.ReadAsStringAsync();
            var imageData = JsonSerializer.Deserialize<UnsplashResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (imageData?.Urls?.Regular == null)
                throw new InvalidOperationException("No image URL found in Unsplash response");

            // Download the actual image bytes
            var imgResp = await _httpClient.GetAsync(imageData.Urls.Regular);
            imgResp.EnsureSuccessStatusCode();
            var bytes = await imgResp.Content.ReadAsByteArrayAsync();

            if (bytes == null || bytes.Length == 0 || !CanDecodeImage(bytes))
            {
                _logger.LogWarning("Unsplash returned empty or undecodable image. Falling back to placeholder.");
                return await CreatePlaceholderOrThrowAsync();
            }

            return bytes;
        }
        catch (Exception ex)
        {
            if (GetConfigBool("Image:UsePlaceholderOnError", true))
            {
                _logger.LogWarning(ex, "Falling back to placeholder image due to Unsplash error.");
                return await CreatePlaceholderOrThrowAsync();
            }

            _logger.LogError(ex, "Failed to fetch image and fallback is disabled.");
            throw;
        }
    }

    private void LogUnsplashRate(HttpResponseMessage resp)
    {
        var limit = resp.Headers.TryGetValues("X-Ratelimit-Limit", out var lim) ? lim.FirstOrDefault() : "unknown";
        var remaining = resp.Headers.TryGetValues("X-Ratelimit-Remaining", out var rem) ? rem.FirstOrDefault() : "unknown";
        _logger.LogWarning("Unsplash 403. X-Ratelimit-Limit={Limit}, X-Ratelimit-Remaining={Remaining}", limit, remaining);
    }

    // replace CreatePlaceholderImageAsync with a more explicit placeholder
    private async Task<byte[]> CreatePlaceholderOrThrowAsync()
    {
        var width = GetConfigInt("Image:PlaceholderWidth", GetConfigInt("Image:MaxWidth", 1280));
        var height = GetConfigInt("Image:PlaceholderHeight", GetConfigInt("Image:MaxHeight", 720));
        var color = ParseRgba(_configuration["Image:PlaceholderColor"]) ?? new Rgba32(45, 45, 45);

        using var img = new Image<Rgba32>(width, height, color);

        var label = _configuration["Image:PlaceholderLabel"] ?? "No image available";
        var fontSize = GetConfigFloat("ImageOverlay:FontSize", 48f);
        var font = GetConfiguredFont(MathF.Max(20f, fontSize * 0.7f), FontStyle.Bold);

        img.Mutate(ctx =>
        {
            // subtle bottom band
            var bandHeight = Math.Max(60, height / 10);
            ctx.Fill(new Color(new Rgba32(0, 0, 0, 90)), new RectangleF(0, height - bandHeight - 20, width, bandHeight));

            // center the label: measure then draw using (string, Font, Color, PointF)
            var measure = TextMeasurer.MeasureSize(label, new TextOptions(font));
            var position = new SixLabors.ImageSharp.PointF(
                (width - (float)measure.Width) / 2f,
                (height - (float)measure.Height) / 2f
            );

            ctx.DrawText(label, font, Color.White, position);
        });

        using var ms = new MemoryStream();
        await img.SaveAsJpegAsync(ms, new JpegEncoder { Quality = Math.Clamp(GetConfigInt("Image:Quality", 85), 30, 100) });
        return ms.ToArray();
    }

    public async Task<byte[]> AddWeatherTextToImageAsync(byte[] imageBytes, WeatherStation station)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);

            // Resize large images to save bandwidth and processing time (keeps aspect ratio)
            var maxWidth = GetConfigInt("Image:MaxWidth", 1280);
            var maxHeight = GetConfigInt("Image:MaxHeight", 720);
            if (image.Width > maxWidth || image.Height > maxHeight)
            {
                image.Mutate(ctx =>
                {
                    ctx.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(maxWidth, maxHeight)
                    });
                });
            }

            // Build overlay text (optionally include region)
            var includeRegion = GetConfigBool("ImageOverlay:IncludeRegion", false);
            var nameLine = includeRegion && !string.IsNullOrWhiteSpace(station.Region)
                ? $"{station.Name} ({station.Region})"
                : station.Name;
            var tempText = station.Temperature.HasValue ? $"{station.Temperature:F1}°C" : "N/A";
            var overlayText = $"{nameLine}\n{tempText}";

            // Fonts: auto-shrink to fit the band
            var baseFontSize = GetConfigFloat("ImageOverlay:FontSize", 48f);
            var wrappingLength = Math.Max(20, image.Width - 40);
            var minFontSize = MathF.Max(14f, baseFontSize * 0.6f); // allow up to ~40% shrink

            float chosenSize = baseFontSize;
            Font testFont = GetConfiguredFont(chosenSize, FontStyle.Bold);

            // Iteratively reduce font size until text fits within the band and wrapping width
            for (;;)
            {
                // Band height scales with font size (same formula as drawing)
                var bandHeight = Math.Max(chosenSize * 3.0f, chosenSize + 20);
                var allowedTextHeight = Math.Max(0, bandHeight - 20);

                var measure = TextMeasurer.MeasureSize(
                    overlayText,
                    new TextOptions(testFont) { WrappingLength = wrappingLength }
                );

                var fitsWidth = measure.Width <= wrappingLength;
                var fitsHeight = measure.Height <= allowedTextHeight;

                if ((fitsWidth && fitsHeight) || chosenSize <= minFontSize)
                    break;

                chosenSize = MathF.Max(minFontSize, chosenSize - 2f);
                testFont = GetConfiguredFont(chosenSize, FontStyle.Bold);
            }

            var overlayFontSize = chosenSize;
            var overlayFont = testFont;

            // Compute bottom band based on final font size
            var finalBandHeight = Math.Max(overlayFontSize * 3.0f, overlayFontSize + 20);
            var bandY = Math.Max(0, image.Height - finalBandHeight - 10);
            var bandRect = new RectangleF(0, bandY, image.Width, Math.Min(image.Height - bandY, finalBandHeight + 10));

            image.Mutate(ctx =>
            {
                ctx.Fill(new Color(new Rgba32(0, 0, 0, 180)), bandRect);

                var textOptions = new RichTextOptions(overlayFont)
                {
                    Origin = new SixLabors.ImageSharp.PointF(20, Math.Max(0, bandRect.Y + 10)),
                    WrappingLength = wrappingLength,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                ctx.DrawText(textOptions, overlayText, Color.White);
            });

            // Watermark + timestamp (place just above band to avoid overlap)
            var watermarkText = _configuration["ImageOverlay:WatermarkText"];
            var includeTimestamp = GetConfigBool("ImageOverlay:IncludeTimestamp", true);
            if (!string.IsNullOrWhiteSpace(watermarkText) || includeTimestamp)
            {
                var tzId = _configuration["ImageOverlay:TimeZoneId"] ?? "Europe/Amsterdam";
                var tz = TryGetTimeZone(tzId) ?? TryGetTimeZone("W. Europe Standard Time");
                var localNow = tz != null ? TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz) : DateTime.UtcNow;
                var ts = includeTimestamp ? localNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : null;
                var combined = string.Join(" · ", new[] { watermarkText, ts }.Where(x => !string.IsNullOrWhiteSpace(x)));

                if (!string.IsNullOrWhiteSpace(combined))
                {
                    var wmFontSize = GetConfigFloat("ImageOverlay:WatermarkFontSize", Math.Max(overlayFontSize * 0.5f, 14f));
                    var watermarkFont = GetConfiguredFont(wmFontSize, FontStyle.Regular);
                    var padding = 14f;

                    var measureOptions = new TextOptions(watermarkFont);
                    var textSize = TextMeasurer.MeasureSize(combined, measureOptions);

                    // place above the band to avoid collision
                    var targetBottom = (float)Math.Max(0, bandRect.Y - 8);
                    var posX = Math.Max(0, image.Width - (float)textSize.Width - padding);
                    var posY = Math.Max(0, targetBottom - (float)textSize.Height - padding);
                    var position = new SixLabors.ImageSharp.PointF(posX, posY);

                    image.Mutate(ctx =>
                    {
                        var rect = new RectangleF(
                            Math.Max(0, position.X - 8),
                            Math.Max(0, position.Y - 4),
                            Math.Min(image.Width, (float)textSize.Width + 16),
                            Math.Min(image.Height, (float)textSize.Height + 8));
                        ctx.Fill(new Color(new Rgba32(0, 0, 0, 120)), rect);
                        ctx.DrawText(combined, watermarkFont, Color.White.WithAlpha(GetConfigFloat("ImageOverlay:WatermarkOpacity", 0.75f)), position);
                    });
                }
            }

            // Save with configurable JPEG quality
            var quality = GetConfigInt("Image:Quality", 85);
            var encoder = new JpegEncoder { Quality = Math.Clamp(quality, 30, 100) };

            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, encoder);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding weather text to image for station {StationName}", station.Name);
            throw;
        }
    }

    public async Task<string> ProcessStationImageAsync(WeatherStation station, string searchKeyword)
    {
        try
        {
            _logger.LogInformation("Processing image for station {StationName}", station.Name);

            // Get image from Unsplash
            var imageBytes = await GetImageAsync(searchKeyword);

            // Add weather overlay (+ resize/watermark per configuration)
            var processedImage = await AddWeatherTextToImageAsync(imageBytes, station);

            // Upload to blob storage
            var blobName = $"{station.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.jpg";
            var blobUrl = await _blobStorageService.UploadImageAsync(processedImage, blobName);

            _logger.LogInformation("Successfully processed and uploaded image for station {StationName}", station.Name);
            return blobUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image for station {StationName}", station.Name);
            throw;
        }
    }

    private int GetConfigInt(string key, int @default)
        => int.TryParse(_configuration[key], out var val) ? val : @default;

    private float GetConfigFloat(string key, float @default)
        => float.TryParse(_configuration[key], out var val) ? val : @default;

    private bool GetConfigBool(string key, bool @default)
        => bool.TryParse(_configuration[key], out var val) ? val : @default;

    private Font GetConfiguredFont(float size, FontStyle style)
    {
        var familyFromConfig = _configuration["ImageOverlay:FontFamily"];
        var fallbacks = new[] { familyFromConfig, "DejaVu Sans", "Liberation Sans", "Noto Sans", "Arial" }
            .Where(s => !string.IsNullOrWhiteSpace(s));

        foreach (var name in fallbacks)
        {
            try
            {
                return SystemFonts.CreateFont(name!, size, style);
            }
            catch
            {
                // try next
            }
        }

        try
        {
            var family = SystemFonts.Families.FirstOrDefault();
            return family.CreateFont(size, style);
        }
        catch
        {
            return SystemFonts.CreateFont("Arial", size, style);
        }
    }

    private class UnsplashResponse
    {
        [JsonPropertyName("urls")]
        public UnsplashUrls? Urls { get; set; }
    }

    private class UnsplashUrls
    {
        [JsonPropertyName("regular")]
        public string? Regular { get; set; }
    }

    private Rgba32? ParseRgba(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        try
        {
            if (s.Length == 6)
            {
                var r = Convert.ToByte(s.Substring(0, 2), 16);
                var g = Convert.ToByte(s.Substring(2, 2), 16);
                var b = Convert.ToByte(s.Substring(4, 2), 16);
                return new Rgba32(r, g, b);
            }
            if (s.Length == 8)
            {
                var a = Convert.ToByte(s.Substring(0, 2), 16);
                var r = Convert.ToByte(s.Substring(2, 2), 16);
                var g = Convert.ToByte(s.Substring(4, 2), 16);
                var b = Convert.ToByte(s.Substring(6, 2), 16);
                return new Rgba32(r, g, b, a);
            }
        }
        catch { /* ignore parse errors */ }
        return null;
    }

    private static TimeZoneInfo? TryGetTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return null; }
    }

    private bool CanDecodeImage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.Load<Rgba32>(stream);
            return image != null && image.Width > 0 && image.Height > 0;
        }
        catch
        {
            return false;
        }
    }
}