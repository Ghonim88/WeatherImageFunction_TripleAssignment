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

            var jobStatus = await _tableStorageService.GetJobStatusAsync(jobId);
            if (jobStatus == null)
            {
                return new NotFoundObjectResult(new { error = "Job not found" });
            }

            var progressPercentage = jobStatus.TotalStations > 0
                ? (int)Math.Round((double)jobStatus.ProcessedStations / jobStatus.TotalStations * 100.0)
                : 0;

            var response = new
            {
                jobStatus.JobId,
                Status = jobStatus.Status.ToString(), // ensure readable enum in API
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