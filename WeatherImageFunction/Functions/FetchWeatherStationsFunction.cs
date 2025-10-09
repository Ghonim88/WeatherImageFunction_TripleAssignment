namespace WeatherImageFunction.Functions;

using System;
using System.Text.Json;
using Azure.Storage.Queues;
using Azure;
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

        var queueName = configuration["ProcessImageQueueName"] ?? "process-image-queue";
        _processImageQueueClient = queueServiceClient.GetQueueClient(queueName);
    }

    [Function("FetchWeatherStations")]
    public async Task Run(
        [QueueTrigger("weather-stations-queue", Connection = "StorageConnectionString")] string queueMessage)
    {
        string? jobId = null;

        try
        {
            _logger.LogInformation("Processing weather stations queue message: {Message}", queueMessage);

            // Ensure queue exists before using it
            await _processImageQueueClient.CreateIfNotExistsAsync();

            // Deserialize queue message
            var message = JsonSerializer.Deserialize<WeatherStationsQueueMessage>(queueMessage);
            
            if (message == null || string.IsNullOrWhiteSpace(message.JobId))
            {
                _logger.LogError("Invalid queue message format");
                return;
            }

            jobId = message.JobId;

            // Update job status to Processing
            var jobStatus = await _tableStorageService.GetJobStatusAsync(jobId);
            if (jobStatus == null)
            {
                _logger.LogError("Job {JobId} not found in table storage", jobId);
                return;
            }

            jobStatus.Status = "Processing";
            await _tableStorageService.UpdateJobStatusAsync(jobStatus);

            _logger.LogInformation("Fetching weather stations for job {JobId}", jobId);

            // Fetch weather stations from Buienradar API
            var stations = await _weatherService.GetWeatherStationsAsync(message.MaxStations);

            if (stations.Count == 0)
            {
                _logger.LogWarning("No weather stations found for job {JobId}", jobId);
                jobStatus.Status = "Failed";
                jobStatus.ErrorMessage = "No weather stations found";
                jobStatus.CompletedAt = DateTime.UtcNow;
                await _tableStorageService.UpdateJobStatusAsync(jobStatus);
                return;
            }

            // Update total stations count
            jobStatus.TotalStations = stations.Count;
            await _tableStorageService.UpdateJobStatusAsync(jobStatus);

            _logger.LogInformation(
                "Found {Count} weather stations for job {JobId}. Queuing image processing tasks...",
                stations.Count,
                jobId);

            // Queue individual image processing tasks
            var queueTasks = new List<Task>();
            foreach (var station in stations)
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
                // Use BinaryData for proper encoding
                queueTasks.Add(_processImageQueueClient.SendMessageAsync(BinaryData.FromString(imageMessageJson)));
            }

            await Task.WhenAll(queueTasks);

            _logger.LogInformation(
                "Successfully queued {Count} image processing tasks for job {JobId}",
                stations.Count,
                jobId);
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
                        jobStatus.Status = "Failed";
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
}