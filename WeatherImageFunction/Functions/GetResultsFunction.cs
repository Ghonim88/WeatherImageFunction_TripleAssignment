namespace WeatherImageFunction.Functions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WeatherImageFunction.Services;

public class GetResultsFunction
{
    private readonly ILogger<GetResultsFunction> _logger;
    private readonly ITableStorageService _tableStorageService;

    public GetResultsFunction(
        ILogger<GetResultsFunction> logger,
        ITableStorageService tableStorageService)
    {
        _logger = logger;
        _tableStorageService = tableStorageService;
    }

    [Function("GetResults")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "jobs/{jobId}")] HttpRequest req,
        string jobId)
    {
        try
        {
            _logger.LogInformation("Fetching results for JobId: {JobId}", jobId);

            // Validate jobId
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return new BadRequestObjectResult(new { error = "JobId is required" });
            }

            if (!Guid.TryParse(jobId, out _))
            {
                return new BadRequestObjectResult(new { error = "Invalid JobId format" });
            }

            // Fetch job status
            var jobStatus = await _tableStorageService.GetJobStatusAsync(jobId);

            if (jobStatus == null)
            {
                _logger.LogWarning("Job not found: {JobId}", jobId);
                return new NotFoundObjectResult(new { error = $"Job with ID '{jobId}' not found" });
            }

            // Calculate progress percentage
            var progressPercentage = jobStatus.TotalStations > 0
                ? (int)((double)jobStatus.ProcessedStations / jobStatus.TotalStations * 100)
                : 0;

            // Build response
            var response = new
            {
                jobStatus.JobId,
                jobStatus.Status,
                jobStatus.TotalStations,
                jobStatus.ProcessedStations,
                ProgressPercentage = progressPercentage,
                jobStatus.ImageUrls,
                TotalImages = jobStatus.ImageUrls.Count,
                jobStatus.CreatedAt,
                jobStatus.CompletedAt,
                DurationSeconds = jobStatus.CompletedAt.HasValue
                    ? (jobStatus.CompletedAt.Value - jobStatus.CreatedAt).TotalSeconds
                    : (DateTime.UtcNow - jobStatus.CreatedAt).TotalSeconds,
                jobStatus.ErrorMessage
            };

            _logger.LogInformation(
                "Job {JobId} - Status: {Status}, Progress: {Processed}/{Total}",
                jobId,
                jobStatus.Status,
                jobStatus.ProcessedStations,
                jobStatus.TotalStations);

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching results for JobId: {JobId}", jobId);
            return new ObjectResult(new { error = "An error occurred while fetching job results" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}