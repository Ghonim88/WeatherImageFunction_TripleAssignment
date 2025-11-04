namespace WeatherImageFunction.Functions;

using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WeatherImageFunction.Models;
using WeatherImageFunction.Services;

public class FetchWeatherStationsFunction
{
    private readonly ILogger<FetchWeatherStationsFunction> _logger;
    private readonly IWeatherService _weatherService;
    private readonly ITableStorageService _tableStorageService;
    private readonly QueueClient _processImageQueueClient;
    private readonly IConfiguration _configuration;

    public FetchWeatherStationsFunction(
        ILogger<FetchWeatherStationsFunction> logger,
        IWeatherService weatherService,
        ITableStorageService tableStorageService,
        QueueServiceClient queueServiceClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _weatherService = weatherService;
        _tableStorageService = tableStorageService;
        _configuration = configuration;

        var queueName = configuration["ProcessImageQueueName"] ?? "process-image-queue";
        _processImageQueueClient = queueServiceClient.GetQueueClient(queueName);
    }

    [Function("FetchWeatherStations")]
    public async Task Run(
        [QueueTrigger("weather-stations-queue", Connection = "AzureWebJobsStorage")] string queueMessage)
    {
        string? jobId = null;

        try
        {
            _logger.LogInformation("Processing weather stations queue message: {Message}", queueMessage);

            await _processImageQueueClient.CreateIfNotExistsAsync();

            var message = JsonSerializer.Deserialize<WeatherStationsQueueMessage>(queueMessage);
            if (message == null || string.IsNullOrWhiteSpace(message.JobId))
            {
                _logger.LogError("Invalid queue message format");
                return;
            }

            jobId = message.JobId;

            var jobStatus = await _tableStorageService.GetJobStatusAsync(jobId);
            if (jobStatus == null)
            {
                _logger.LogError("Job {JobId} not found in table storage", jobId);
                return;
            }

            jobStatus.Status = JobState.Processing;
            await _tableStorageService.UpdateJobStatusAsync(jobStatus);

            _logger.LogInformation("Fetching weather stations for job {JobId}", jobId);

            // Pull extra to allow filtering
            var fetchCount = Math.Max(message.MaxStations, 200);
            var stations = await _weatherService.GetWeatherStationsAsync(fetchCount);

            if (stations.Count == 0)
            {
                _logger.LogWarning("No weather stations found for job {JobId}", jobId);
                jobStatus.Status = JobState.Failed;
                jobStatus.ErrorMessage = "No weather stations found";
                jobStatus.CompletedAt = DateTime.UtcNow;
                await _tableStorageService.UpdateJobStatusAsync(jobStatus);
                return;
            }

            var strictMatch = bool.TryParse(_configuration["CityFilter:Strict"], out var s) && s;
            var fallbackMax = int.TryParse(_configuration["CityFilter:FallbackMaxStations"], out var fcap) ? Math.Max(0, fcap) : 5;
            var unsplashMax = int.TryParse(_configuration["Unsplash:MaxRequestsPerJob"], out var ucap) ? Math.Max(1, ucap) : 45;

            var filtered = stations;

            if (!string.IsNullOrWhiteSpace(message.City))
            {
                var cityNorm = NormalizeForCompare(message.City);
                _logger.LogInformation("City filter requested: '{City}' (normalized: '{CityNorm}')", message.City, cityNorm);

                // 1) Exact-first on regio
                var exactRegion = stations
                    .Where(s => NormalizeForCompare(s.Region) == cityNorm)
                    .ToList();

                // 2) If no exact, try contains on regio or stationname
                if (exactRegion.Count == 0)
                {
                    var contains = stations.Where(s =>
                    {
                        var nameNorm = NormalizeForCompare(s.Name);
                        var regionNorm = NormalizeForCompare(s.Region);
                        return (!string.IsNullOrEmpty(regionNorm) && regionNorm.Contains(cityNorm))
                               || (!string.IsNullOrEmpty(nameNorm) && nameNorm.Contains(cityNorm));
                    }).ToList();

                    filtered = contains;
                }
                else
                {
                    filtered = exactRegion;
                }

                // 3) If still none, strict or capped fallback
                if (filtered.Count == 0)
                {
                    if (strictMatch)
                    {
                        _logger.LogWarning("No stations matched city '{City}' for job {JobId}; strict mode -> failing job.", message.City, jobId);
                        jobStatus.Status = JobState.Failed;
                        jobStatus.ErrorMessage = $"No stations matched city '{message.City}'.";
                        jobStatus.CompletedAt = DateTime.UtcNow;
                        await _tableStorageService.UpdateJobStatusAsync(jobStatus);
                        return;
                    }

                    _logger.LogWarning("No stations matched city '{City}' for job {JobId}. Falling back with cap {Cap}.", message.City, jobId, fallbackMax);
                    filtered = stations.Take(Math.Max(0, fallbackMax)).ToList();
                }
            }

            // Enforce per-job cap and requested cap
            var hardCap = Math.Max(1, Math.Min(message.MaxStations, unsplashMax));
            var toQueue = filtered.Take(hardCap).ToList();

            // Log what we matched (first 5) for debugging
            var peek = string.Join(" | ", toQueue.Take(5).Select(s => $"{s.Name} ({s.Region})"));
            _logger.LogInformation("Matched {Count} stations. Sample: {Sample}", toQueue.Count, peek);

            jobStatus.TotalStations = toQueue.Count;
            await _tableStorageService.UpdateJobStatusAsync(jobStatus);

            _logger.LogInformation("Queuing {Count} image processing tasks for job {JobId}", toQueue.Count, jobId);

            var queueTasks = new List<Task>();
            foreach (var station in toQueue)
            {
                var imageMessage = new ProcessImageQueueMessage
                {
                    JobId = jobId,
                    StationId = station.Id,
                    StationName = station.Name,
                    Latitude = station.Latitude,
                    Longitude = station.Longitude,
                    Temperature = station.Temperature,
                    Region = station.Region,
                    SearchKeyword = message.SearchKeyword
                };

                var imageMessageJson = JsonSerializer.Serialize(imageMessage);
                queueTasks.Add(_processImageQueueClient.SendMessageAsync(BinaryData.FromString(imageMessageJson)));
            }

            await Task.WhenAll(queueTasks);

            _logger.LogInformation("Successfully queued {Count} image processing tasks for job {JobId}", toQueue.Count, jobId);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize queue message");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing weather stations for job {JobId}", jobId);

            if (!string.IsNullOrWhiteSpace(jobId))
            {
                try
                {
                    var jobStatus = await _tableStorageService.GetJobStatusAsync(jobId);
                    if (jobStatus != null)
                    {
                        jobStatus.Status = JobState.Failed;
                        jobStatus.ErrorMessage = $"Error fetching weather stations: {ex.Message}";
                        jobStatus.CompletedAt = DateTime.UtcNow;
                        await _tableStorageService.UpdateJobStatusAsync(jobStatus);
                    }
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "Failed to update job status for job {JobId}", jobId);
                }
            }
        }
    }

    private static string NormalizeForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        // Strip diacritics
        var formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        var noDiacritics = sb.ToString().Normalize(NormalizationForm.FormC);

        // Drop punctuation/whitespace, lower-case
        var cleaned = new string(noDiacritics.Where(c => !char.IsPunctuation(c) && !char.IsWhiteSpace(c)).ToArray());
        return cleaned.ToLowerInvariant();
    }
}