using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp.PixelFormats;
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
            {
                throw new InvalidOperationException("Unsplash Access Key is not configured");
            }

            var searchUrl = $"https://api.unsplash.com/photos/random?query={Uri.EscapeDataString(searchKeyword)}&client_id={unsplashAccessKey}";
            
            _logger.LogInformation("Fetching image from Unsplash with keyword: {Keyword}", searchKeyword);

            var response = await _httpClient.GetAsync(searchUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Unsplash API response: {Response}", content.Length > 200 ? content.Substring(0, 200) + "..." : content);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var imageData = JsonSerializer.Deserialize<UnsplashResponse>(content, options);

            if (imageData?.Urls?.Regular == null)
            {
                _logger.LogError("Failed to parse Unsplash response. Response content: {Content}", content);
                throw new InvalidOperationException("No image URL found in Unsplash response");
            }

            _logger.LogInformation("Image URL from Unsplash: {Url}", imageData.Urls.Regular);

            // Download the actual image
            var imageResponse = await _httpClient.GetAsync(imageData.Urls.Regular);
            imageResponse.EnsureSuccessStatusCode();

            return await imageResponse.Content.ReadAsByteArrayAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while fetching image from Unsplash");
            throw;
        }
    }

    public async Task<byte[]> AddWeatherTextToImageAsync(byte[] imageBytes, WeatherStation station)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            
            // Create weather text overlay
            var tempText = station.Temperature.HasValue 
                ? $"{station.Temperature:F1}°C" 
                : "N/A";
            var overlayText = $"{station.Name}\n{tempText}";

            // Use system fonts - works on both Windows and Linux
            var font = SystemFonts.CreateFont("Arial", 48, FontStyle.Bold);

            var textOptions = new RichTextOptions(font)
            {
                Origin = new SixLabors.ImageSharp.PointF(20, image.Height - 120),
                WrappingLength = image.Width - 40,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Add semi-transparent background for text
            image.Mutate(ctx =>
            {
                // Draw background rectangle
                ctx.Fill(new Color(new Rgba32(0, 0, 0, 180)), 
                    new RectangleF(0, image.Height - 140, image.Width, 140));
                
                // Draw text
                ctx.DrawText(textOptions, overlayText, Color.White);
            });

            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms);
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

            // Add weather overlay
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

    // Helper class for Unsplash API response
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
}