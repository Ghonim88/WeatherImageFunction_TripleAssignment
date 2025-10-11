using System.Globalization;
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
using SixLabors.ImageSharp.Drawing; // for gradients if desired

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
            {
                throw new InvalidOperationException("Unsplash Access Key is not configured");
            }

            var searchUrl = $"https://api.unsplash.com/photos/random?query={Uri.EscapeDataString(searchKeyword)}&client_id={unsplashAccessKey}";
            _logger.LogInformation("Fetching image from Unsplash with keyword: {Keyword}", searchKeyword);

            using var metaResponse = await GetWithRetryAsync(searchUrl);
            metaResponse.EnsureSuccessStatusCode();

            var content = await metaResponse.Content.ReadAsStringAsync();
            var imageData = JsonSerializer.Deserialize<UnsplashResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (imageData?.Urls?.Regular == null)
            {
                throw new InvalidOperationException("No image URL found in Unsplash response");
            }

            using var imageResponse = await GetWithRetryAsync(imageData.Urls.Regular);
            imageResponse.EnsureSuccessStatusCode();

            return await imageResponse.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            // Fallback if enabled
            var useFallback = GetConfigBool("Image:UsePlaceholderOnError", true);
            if (useFallback)
            {
                _logger.LogWarning(ex, "Falling back to placeholder image due to Unsplash error.");
                var width = GetConfigInt("Image:PlaceholderWidth", GetConfigInt("Image:MaxWidth", 1280));
                var height = GetConfigInt("Image:PlaceholderHeight", GetConfigInt("Image:MaxHeight", 720));
                var color = ParseRgba(_configuration["Image:PlaceholderColor"]) ?? new Rgba32(45, 45, 45);
                return await CreatePlaceholderImageAsync(width, height, color);
            }

            _logger.LogError(ex, "Failed to fetch image and fallback is disabled.");
            throw;
        }
    }

    // replace CreatePlaceholderImageAsync with a more explicit placeholder
    private async Task<byte[]> CreatePlaceholderImageAsync(int width, int height, Rgba32 bg)
    {
        using var img = new Image<Rgba32>(width, height, bg);

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

            // Prepare weather text overlay
            var tempText = station.Temperature.HasValue ? $"{station.Temperature:F1}°C" : "N/A";
            var overlayText = $"{station.Name}\n{tempText}";

            // Configurable font (fallbacks for Linux containers)
            var overlayFontSize = GetConfigFloat("ImageOverlay:FontSize", 48f);
            var overlayFont = GetConfiguredFont(overlayFontSize, FontStyle.Bold);

            var textOptions = new RichTextOptions(overlayFont)
            {
                Origin = new SixLabors.ImageSharp.PointF(20, image.Height - (overlayFontSize * 2.8f)),
                WrappingLength = image.Width - 40,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Add semi-transparent background for main overlay
            image.Mutate(ctx =>
            {
                var bgHeight = overlayFontSize * 3.0f;
                ctx.Fill(new Color(new Rgba32(0, 0, 0, 180)),
                    new RectangleF(0, image.Height - bgHeight - 10, image.Width, bgHeight + 10));
                ctx.DrawText(textOptions, overlayText, Color.White);
            });

            // Optional: watermark and/or timestamp (bottom-right)
            var watermarkText = _configuration["ImageOverlay:WatermarkText"];
            var includeTimestamp = GetConfigBool("ImageOverlay:IncludeTimestamp", true);
            if (!string.IsNullOrWhiteSpace(watermarkText) || includeTimestamp)
            {
                var tzId = _configuration["ImageOverlay:TimeZoneId"] ?? "Europe/Amsterdam";
                var tz = TryGetTimeZone(tzId) ?? TryGetTimeZone("W. Europe Standard Time"); // Windows fallback
                var localNow = tz != null ? TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz) : DateTime.UtcNow;
                var ts = includeTimestamp ? localNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + (tz != null ? $" {tz.StandardName}" : " UTC") : null;
                var combined = string.Join(" · ", new[] { watermarkText, ts }.Where(x => !string.IsNullOrWhiteSpace(x)));

                if (!string.IsNullOrWhiteSpace(combined))
                {
                    var wmFontSize = GetConfigFloat("ImageOverlay:WatermarkFontSize", Math.Max(overlayFontSize * 0.5f, 14f));
                    var watermarkFont = GetConfiguredFont(wmFontSize, FontStyle.Regular);
                    var padding = 14f;

                    var measureOptions = new TextOptions(watermarkFont);
                    var textSize = TextMeasurer.MeasureSize(combined, measureOptions);

                    var position = new SixLabors.ImageSharp.PointF(
                        image.Width - (float)textSize.Width - padding,
                        image.Height - (float)textSize.Height - padding
                    );

                    image.Mutate(ctx =>
                    {
                        var rect = new RectangleF(
                            position.X - 8,
                            position.Y - 4,
                            (float)textSize.Width + 16,
                            (float)textSize.Height + 8);
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

    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, int maxAttempts = 3)
    {
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);

                if ((int)response.StatusCode == 429)
                {
                    var ra = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
                    _logger.LogWarning("Rate limited by Unsplash (429). Retrying after {Delay}s (attempt {Attempt}/{Max})",
                        ra.TotalSeconds, attempt, maxAttempts);
                    await Task.Delay(ra);
                    continue;
                }

                if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    _logger.LogWarning("Server error {Status}. Retrying in {Delay}s (attempt {Attempt}/{Max})",
                        response.StatusCode, delay.TotalSeconds, attempt, maxAttempts);
                    await Task.Delay(delay);
                    delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "HTTP request failed. Retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt, maxAttempts);
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
            }
        }

        return await _httpClient.GetAsync(url);
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
}