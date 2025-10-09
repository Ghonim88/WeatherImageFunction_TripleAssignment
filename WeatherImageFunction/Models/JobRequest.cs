namespace WeatherImageFunction.Models;

public class JobRequest
{
    public string? City { get; set; }
    public string? SearchKeyword { get; set; }
    public int MaxStations { get; set; } = 50;
}