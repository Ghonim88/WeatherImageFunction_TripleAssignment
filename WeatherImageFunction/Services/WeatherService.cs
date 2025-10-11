namespace WeatherImageFunction.Services;

using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WeatherImageFunction.Models;

public class WeatherService : IWeatherService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WeatherService> _logger;
    private readonly string _buienradarApiUrl;

    public WeatherService(HttpClient httpClient, ILogger<WeatherService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _buienradarApiUrl = configuration["BuienradarApiUrl"]
            ?? "https://data.buienradar.nl/2.0/feed/json"; // fallback default
    }

    public async Task<List<WeatherStation>> GetWeatherStationsAsync(int maxStations = 50)
    {
        try
        {
            _logger.LogInformation("Fetching weather stations from Buienradar API: {Url}", _buienradarApiUrl);

            var response = await _httpClient.GetAsync(_buienradarApiUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var buienradarData = JsonSerializer.Deserialize<BuienradarResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (buienradarData?.Actual?.Stationmeasurements == null)
            {
                _logger.LogWarning("No station measurements found in API response");
                return new List<WeatherStation>();
            }

            var stations = buienradarData.Actual.Stationmeasurements
                .Where(s => !string.IsNullOrEmpty(s.Stationname))
                .Take(maxStations)
                .Select(s => new WeatherStation
                {
                    Id = s.Stationid,
                    Name = s.Stationname!,
                    Latitude = s.Lat,
                    Longitude = s.Lon,
                    Temperature = s.Temperature,
                    Region = s.Regio
                })
                .ToList();

            _logger.LogInformation("Successfully fetched {Count} weather stations", stations.Count);
            return stations;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while fetching weather stations");
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error while parsing Buienradar response");
            throw;
        }
    }

    public async Task<WeatherStation?> GetStationByIdAsync(int stationId)
    {
        var stations = await GetWeatherStationsAsync();
        return stations.FirstOrDefault(s => s.Id == stationId);
    }
}