namespace WeatherImageFunction.Functions.HttpTriggers;

using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WeatherImageFunction.Models;

public class ListJobsFunction
{
    private readonly ILogger<ListJobsFunction> _logger;
    private readonly TableClient _tableClient;

    public ListJobsFunction(
        ILogger<ListJobsFunction> logger,
        TableServiceClient tableServiceClient,
        IConfiguration configuration)
    {
        _logger = logger;
        var tableName = configuration["TableName"] ?? "JobStatus";
        _tableClient = tableServiceClient.GetTableClient(tableName);
    }

    [Function("ListJobs")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "jobs")] HttpRequest req)
    {
        try
        {
            _logger.LogInformation("Fetching all jobs");

            // Parse query parameters
            var topParam = req.Query["top"].ToString();
            var top = int.TryParse(topParam, out var t) && t > 0 && t <= 100 ? t : 20;

            var jobs = new List<object>();
            var query = _tableClient.QueryAsync<TableEntity>(
                filter: "PartitionKey eq 'JobStatus'",
                maxPerPage: top);

            await foreach (var entity in query)
            {
                var job = new
                {
                    JobId = entity.RowKey,
                    Status = entity.GetString(nameof(JobStatus.Status)),
                    TotalStations = entity.GetInt32(nameof(JobStatus.TotalStations)) ?? 0,
                    ProcessedStations = entity.GetInt32(nameof(JobStatus.ProcessedStations)) ?? 0,
                    ImageCount = JsonSerializer.Deserialize<List<string>>(
                        entity.GetString(nameof(JobStatus.ImageUrls)) ?? "[]")?.Count ?? 0,
                    CreatedAt = entity.GetDateTime(nameof(JobStatus.CreatedAt)),
                    CompletedAt = entity.GetDateTime(nameof(JobStatus.CompletedAt))
                };
                jobs.Add(job);
            }

            _logger.LogInformation("Retrieved {Count} jobs", jobs.Count);

            return new OkObjectResult(new
            {
                TotalCount = jobs.Count,
                Jobs = jobs.OrderByDescending(j => ((dynamic)j).CreatedAt).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching jobs list");
            return new ObjectResult(new { error = "An error occurred while fetching jobs" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}