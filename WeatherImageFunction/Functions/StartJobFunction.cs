namespace WeatherImageFunction.Functions;

using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WeatherImageFunction.Models;
using WeatherImageFunction.Services;
using System;

public class StartJobFunction
{
    private readonly ILogger<StartJobFunction> _logger;
    private readonly ITableStorageService _tableStorageService;
    private readonly QueueClient _queueClient;
    private readonly QueueServiceClient _queueServiceClient;
    private readonly IConfiguration _configuration;

    public StartJobFunction(
        ILogger<StartJobFunction> logger,
        ITableStorageService tableStorageService,
        QueueServiceClient queueServiceClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _tableStorageService = tableStorageService;
        _queueServiceClient = queueServiceClient;
        _configuration = configuration;

        var queueName = configuration["WeatherStationsQueueName"] ?? "weather-stations-queue";
        _queueClient = queueServiceClient.GetQueueClient(queueName);
    }

    [Function("StartJob")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "jobs")] HttpRequest req)
    {
        try
        {
            _logger.LogInformation("Starting new weather image job");

            // Ensure queue exists
            await _queueClient.CreateIfNotExistsAsync();

            // Also create process-image-queue upfront
            var processImageQueueName = _configuration["ProcessImageQueueName"] ?? "process-image-queue";
            var processImageQueue = _queueServiceClient.GetQueueClient(processImageQueueName);
            await processImageQueue.CreateIfNotExistsAsync();

            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            JobRequest? jobRequest;
            try
            {
                jobRequest = JsonSerializer.Deserialize<JobRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON in request body");
                return new BadRequestObjectResult(new { error = "Invalid JSON format" });
            }

            // Validate request
            if (jobRequest == null)
            {
                return new BadRequestObjectResult(new { error = "Request body is required" });
            }

            if (string.IsNullOrWhiteSpace(jobRequest.SearchKeyword))
            {
                return new BadRequestObjectResult(new { error = "SearchKeyword is required" });
            }

            if (jobRequest.MaxStations <= 0 || jobRequest.MaxStations > 100)
            {
                return new BadRequestObjectResult(new { error = "MaxStations must be between 1 and 100" });
            }

            // Generate unique job ID
            var jobId = Guid.NewGuid().ToString();

            // Create initial job status
            var jobStatus = new JobStatus
            {
                JobId = jobId,
                Status = JobState.Pending,
                TotalStations = jobRequest.MaxStations,
                ProcessedStations = 0,
                CreatedAt = DateTime.UtcNow
            };

            await _tableStorageService.CreateJobStatusAsync(jobStatus);
            _logger.LogInformation("Created job status for JobId: {JobId}", jobId);

            // Create queue message to trigger station fetching
            var queueMessage = new
            {
                JobId = jobId,
                jobRequest.SearchKeyword,
                jobRequest.MaxStations,
                City = jobRequest.City ?? string.Empty
            };

            var messageJson = JsonSerializer.Serialize(queueMessage);
            // Use BinaryData for proper encoding
            await _queueClient.SendMessageAsync(BinaryData.FromString(messageJson));

            _logger.LogInformation("Queued weather stations fetch for JobId: {JobId}", jobId);

            // Return response (keep Status as string for client readability)
            var response = new JobResponse
            {
                JobId = jobId,
                Status = JobState.Pending.ToString(),
                Message = "Job created successfully. Processing has started.",
                CreatedAt = DateTime.UtcNow
            };

            return new AcceptedResult($"/api/jobs/{jobId}", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting weather image job");
            return new ObjectResult(new { error = "An error occurred while starting the job" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}