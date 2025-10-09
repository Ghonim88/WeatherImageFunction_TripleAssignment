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

        try
        {
            _logger.LogInformation("Processing image queue message: {Message}", queueMessage);

            // Deserialize queue message
            var message = JsonSerializer.Deserialize<ProcessImageQueueMessage>(queueMessage);

            if (message == null || string.IsNullOrWhiteSpace(message.JobId))
            {
                _logger.LogError("Invalid queue message format");
                return;
            }

            jobId = message.JobId;
            stationName = message.StationName;

            _logger.LogInformation(
                "Processing image for station {StationName} (ID: {StationId}) in job {JobId}",
                message.StationName,
                message.StationId,
                jobId);

            // Create WeatherStation object from message
            var station = new WeatherStation
            {
                Id = message.StationId,
                Name = message.StationName,
                Latitude = message.Latitude,
                Longitude = message.Longitude,
                Temperature = message.Temperature,
                Region = message.Region
            };

            _logger.LogInformation("About to call ProcessStationImageAsync...");
            
            // Process the image (fetch from Unsplash, add overlay, upload to blob)
            var imageUrl = await _imageService.ProcessStationImageAsync(station, message.SearchKeyword);

            _logger.LogInformation(
                "Successfully processed image for station {StationName}. URL: {ImageUrl}",
                message.StationName,
                imageUrl);

            _logger.LogInformation("About to add image URL to job...");
            
            // Update job status with the new image URL
            await _tableStorageService.AddImageUrlToJobAsync(jobId, imageUrl);

            _logger.LogInformation("Image URL added to job successfully");

            // Check if job is complete
            var jobStatus = await _tableStorageService.GetJobStatusAsync(jobId);
            if (jobStatus != null && jobStatus.ProcessedStations >= jobStatus.TotalStations)
            {
                jobStatus.Status = "Completed";
                jobStatus.CompletedAt = DateTime.UtcNow;
                await _tableStorageService.UpdateJobStatusAsync(jobStatus);

                _logger.LogInformation(
                    "Job {JobId} completed. Processed {Count} stations in {Duration} seconds",
                    jobId,
                    jobStatus.TotalStations,
                    (jobStatus.CompletedAt.Value - jobStatus.CreatedAt).TotalSeconds);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize queue message");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing image for station {StationName} in job {JobId}",
                stationName ?? "Unknown",
                jobId ?? "Unknown");

            // Note: We don't mark the entire job as failed if one image fails
            // The job can still complete with partial results
            _logger.LogWarning(
                "Image processing failed for station {StationName}, but job {JobId} will continue",
                stationName ?? "Unknown",
                jobId ?? "Unknown");
        }
        
        _logger.LogInformation("=== QUEUE TRIGGER COMPLETED ===");
    }

}