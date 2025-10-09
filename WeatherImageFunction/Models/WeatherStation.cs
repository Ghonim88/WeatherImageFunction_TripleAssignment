namespace WeatherImageFunction.Models;

public class WeatherStation
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Temperature { get; set; }
    public string? Region { get; set; }
}