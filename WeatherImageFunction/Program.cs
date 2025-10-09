using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WeatherImageFunction.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Application Insights
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Configuration
var configuration = builder.Configuration;

// Azure Storage Services
builder.Services.AddSingleton(sp =>
{
    var connectionString = configuration["StorageConnectionString"]
        ?? throw new InvalidOperationException("StorageConnectionString is not configured");
    return new BlobServiceClient(connectionString);
});

builder.Services.AddSingleton(sp =>
{
    var connectionString = configuration["StorageConnectionString"]
        ?? throw new InvalidOperationException("StorageConnectionString is not configured");
    return new QueueServiceClient(connectionString);
});

builder.Services.AddSingleton(sp =>
{
    var connectionString = configuration["StorageConnectionString"]
        ?? throw new InvalidOperationException("StorageConnectionString is not configured");
    return new TableServiceClient(connectionString);
});

// HttpClient for external API calls
builder.Services.AddHttpClient<IWeatherService, WeatherService>((client) =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "WeatherImageFunction/1.0");
});

builder.Services.AddHttpClient<IImageService, ImageService>((client) =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("User-Agent", "WeatherImageFunction/1.0");
});

// Application Services
builder.Services.AddScoped<IWeatherService, WeatherService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<ITableStorageService, TableStorageService>();

// Logging
builder.Services.AddLogging();

builder.Build().Run();