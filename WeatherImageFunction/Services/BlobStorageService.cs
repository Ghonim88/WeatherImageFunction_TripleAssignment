namespace WeatherImageFunction.Services;

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly string _containerName;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        ILogger<BlobStorageService> logger,
        IConfiguration configuration)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
        _containerName = configuration["BlobContainerName"] ?? "weather-images";
    }

    public async Task<string> UploadImageAsync(byte[] imageBytes, string blobName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            var blobClient = containerClient.GetBlobClient(blobName);

            using var stream = new MemoryStream(imageBytes);
            
            // FIXED: Removed named parameter conflict
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "image/jpeg"
                }
            };
            
            await blobClient.UploadAsync(stream, uploadOptions, cancellationToken: default);

            _logger.LogInformation("Uploaded blob: {BlobName}", blobName);

            // Return SAS URL
            return await GetBlobSasUrlAsync(blobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading blob: {BlobName}", blobName);
            throw;
        }
    }

    public async Task<string> GetBlobSasUrlAsync(string blobName, int expiryHours = 24)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                throw new FileNotFoundException($"Blob {blobName} not found");
            }

            // Check if using local development storage (Azurite)
            if (_blobServiceClient.Uri.Host.Contains("127.0.0.1") || _blobServiceClient.Uri.Host.Contains("localhost"))
            {
                // For local development, return the direct blob URL
                _logger.LogWarning("Using local storage emulator - returning direct blob URL without SAS");
                return blobClient.Uri.ToString();
            }

            // For Azure Storage, generate SAS URL
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _containerName,
                BlobName = blobName,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(expiryHours)
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUrl = blobClient.GenerateSasUri(sasBuilder);
            return sasUrl.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating SAS URL for blob: {BlobName}", blobName);
            throw;
        }
    }

    public async Task<bool> BlobExistsAsync(string blobName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        return await blobClient.ExistsAsync();
    }

    public async Task DeleteBlobAsync(string blobName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
            
            _logger.LogInformation("Deleted blob: {BlobName}", blobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting blob: {BlobName}", blobName);
            throw;
        }
    }
}