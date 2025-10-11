namespace WeatherImageFunction.Services;

using WeatherImageFunction.Models;

public interface ITableStorageService
{
    Task CreateJobStatusAsync(JobStatus jobStatus);
    Task<JobStatus?> GetJobStatusAsync(string jobId);
    Task UpdateJobStatusAsync(JobStatus jobStatus);
    Task AddImageUrlToJobAsync(string jobId, string imageUrl);
    Task IncrementProcessedStationsAsync(string jobId);
}