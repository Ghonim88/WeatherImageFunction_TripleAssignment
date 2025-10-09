namespace WeatherImageFunction.Models;

public class BuienradarResponse
{
    public Actual? Actual { get; set; }
}

public class Actual
{
    public List<StationMeasurement>? Stationmeasurements { get; set; }
}

public class StationMeasurement
{
    public int Stationid { get; set; }
    public string? Stationname { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string? Regio { get; set; }
    public double? Temperature { get; set; }
}