namespace WeatherImageFunction.Services;

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WeatherImageFunction.Models;

public class TableStorageService : ITableStorageService
{
    private readonly TableClient _tableClient;
    private readonly ILogger<TableStorageService> _logger;

    public TableStorageService(
        TableServiceClient tableServiceClient,
        ILogger<TableStorageService> logger,
        IConfiguration configuration)
    {
        var tableName = configuration["TableName"] ?? "JobStatus";
        _tableClient = tableServiceClient.GetTableClient(tableName);
        _tableClient.CreateIfNotExists();
        _logger = logger;
    }

    public async Task CreateJobStatusAsync(JobStatus jobStatus)
    {
        try
        {
            var entity = new TableEntity("JobStatus", jobStatus.JobId)
            {
                { nameof(JobStatus.Status), jobStatus.Status },
                { nameof(JobStatus.TotalStations), jobStatus.TotalStations },
                { nameof(JobStatus.ProcessedStations), jobStatus.ProcessedStations },
                { nameof(JobStatus.ImageUrls), JsonSerializer.Serialize(jobStatus.ImageUrls) },
                { nameof(JobStatus.CreatedAt), jobStatus.CreatedAt },
                { nameof(JobStatus.CompletedAt), jobStatus.CompletedAt },
                { nameof(JobStatus.ErrorMessage), jobStatus.ErrorMessage }
            };

            await _tableClient.AddEntityAsync(entity);
            _logger.LogInformation("Created job status for JobId: {JobId}", jobStatus.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating job status for JobId: {JobId}", jobStatus.JobId);
            throw;
        }
    }

    public async Task<JobStatus?> GetJobStatusAsync(string jobId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>("JobStatus", jobId);
            var entity = response.Value;

            return new JobStatus
            {
                JobId = jobId,
                Status = entity.GetString(nameof(JobStatus.Status)) ?? "Unknown",
                TotalStations = entity.GetInt32(nameof(JobStatus.TotalStations)) ?? 0,
                ProcessedStations = entity.GetInt32(nameof(JobStatus.ProcessedStations)) ?? 0,
                ImageUrls = JsonSerializer.Deserialize<List<string>>(
                    entity.GetString(nameof(JobStatus.ImageUrls)) ?? "[]") ?? new List<string>(),
                CreatedAt = entity.GetDateTime(nameof(JobStatus.CreatedAt)) ?? DateTime.UtcNow,
                CompletedAt = entity.GetDateTime(nameof(JobStatus.CompletedAt)),
                ErrorMessage = entity.GetString(nameof(JobStatus.ErrorMessage))
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Job status not found for JobId: {JobId}", jobId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job status for JobId: {JobId}", jobId);
            throw;
        }
    }

    public async Task UpdateJobStatusAsync(JobStatus jobStatus)
    {
        try
        {
            var entity = new TableEntity("JobStatus", jobStatus.JobId)
            {
                { nameof(JobStatus.Status), jobStatus.Status },
                { nameof(JobStatus.TotalStations), jobStatus.TotalStations },
                { nameof(JobStatus.ProcessedStations), jobStatus.ProcessedStations },
                { nameof(JobStatus.ImageUrls), JsonSerializer.Serialize(jobStatus.ImageUrls) },
                { nameof(JobStatus.CreatedAt), jobStatus.CreatedAt },
                { nameof(JobStatus.CompletedAt), jobStatus.CompletedAt },
                { nameof(JobStatus.ErrorMessage), jobStatus.ErrorMessage }
            };

            await _tableClient.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace);
            _logger.LogInformation("Updated job status for JobId: {JobId}", jobStatus.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating job status for JobId: {JobId}", jobStatus.JobId);
            throw;
        }
    }

    public async Task AddImageUrlToJobAsync(string jobId, string imageUrl)
    {
        try
        {
            var jobStatus = await GetJobStatusAsync(jobId);
            if (jobStatus != null)
            {
                jobStatus.ImageUrls.Add(imageUrl);
                jobStatus.ProcessedStations++;
                await UpdateJobStatusAsync(jobStatus);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding image URL to job {JobId}", jobId);
            throw;
        }
    }
}