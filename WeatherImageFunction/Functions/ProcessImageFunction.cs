namespace WeatherImageFunction.Functions.QueueTriggers;

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WeatherImageFunction.Models;
using WeatherImageFunction.Services;

public class ProcessImageFunction
{
    private readonly ILogger<ProcessImageFunction> _logger;
    private readonly IImageService _imageService;
    private readonly ITableStorageService _tableStorageService;

    public ProcessImageFunction(
        ILogger<ProcessImageFunction> logger,
        IImageService imageService,
        ITableStorageService tableStorageService)
    {
        _logger = logger;
        _imageService = imageService;
        _tableStorageService = tableStorageService;
    }

    [Function("ProcessImage")]
    public async Task Run(
        [QueueTrigger("process-image-queue", Connection = "StorageConnectionString")] string queueMessage)
    {
        _logger.LogInformation("=== QUEUE TRIGGER ACTIVATED ===");
        _logger.LogInformation("Raw queue message: {RawMessage}", queueMessage);

        string? jobId = null;
        string? stationName = null;
        bool success = false;

        try
        {
            var message = JsonSerializer.Deserialize<ProcessImageQueueMessage>(queueMessage);
            if (message == null || string.IsNullOrWhiteSpace(message.JobId))
            {
                _logger.LogError("Invalid queue message format");
                return;
            }

            jobId = message.JobId;
            stationName = message.StationName;

            var station = new WeatherStation
            {
                Id = message.StationId,
                Name = message.StationName,
                Latitude = message.Latitude,
                Longitude = message.Longitude,
                Temperature = message.Temperature,
                Region = message.Region
            };

            _logger.LogInformation("About to call ProcessStationImageAsync for {StationName}...", station.Name);

            var imageUrl = await _imageService.ProcessStationImageAsync(station, message.SearchKeyword);
            _logger.LogInformation("Successfully processed image for {StationName}. URL: {ImageUrl}", station.Name, imageUrl);

            await _tableStorageService.AddImageUrlToJobAsync(jobId, imageUrl);
            _logger.LogInformation("Image URL added to job successfully");
            success = true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize queue message");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image for station {StationName} in job {JobId}", stationName ?? "Unknown", jobId ?? "Unknown");
            _logger.LogWarning("Image processing failed for station {StationName}, but job {JobId} will continue", stationName ?? "Unknown", jobId ?? "Unknown");
        }
        finally
        {
            // Ensure progress advances even on failures
            if (!string.IsNullOrWhiteSpace(jobId))
            {
                if (!success)
                {
                    await _tableStorageService.IncrementProcessedStationsAsync(jobId);
                }

                var jobStatus = await _tableStorageService.GetJobStatusAsync(jobId);
                if (jobStatus != null && jobStatus.ProcessedStations >= jobStatus.TotalStations)
                {
                    jobStatus.Status = JobState.Completed;
                    jobStatus.CompletedAt = DateTime.UtcNow;
                    await _tableStorageService.UpdateJobStatusAsync(jobStatus);

                    _logger.LogInformation("Job {JobId} completed. Processed {Count} stations in {Duration} seconds",
                        jobId,
                        jobStatus.TotalStations,
                        (jobStatus.CompletedAt.Value - jobStatus.CreatedAt).TotalSeconds);
                }
            }

            _logger.LogInformation("=== QUEUE TRIGGER COMPLETED ===");
        }
    }
}