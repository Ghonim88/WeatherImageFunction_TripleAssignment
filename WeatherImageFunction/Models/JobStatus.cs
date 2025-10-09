namespace WeatherImageFunction.Models;

public class JobStatus
{
    public required string JobId { get; set; }
    public required string Status { get; set; } // Pending, Processing, Completed, Failed
    public int TotalStations { get; set; }
    public int ProcessedStations { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}