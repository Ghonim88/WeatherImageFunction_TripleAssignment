namespace WeatherImageFunction.Models;

public class JobResponse
{
    public required string JobId { get; set; }
    public required string Status { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}   