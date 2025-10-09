namespace WeatherImageFunction.Services;

public interface IBlobStorageService
{
    Task<string> UploadImageAsync(byte[] imageBytes, string blobName);
    Task<string> GetBlobSasUrlAsync(string blobName, int expiryHours = 24);
    Task<bool> BlobExistsAsync(string blobName);
    Task DeleteBlobAsync(string blobName);
}