namespace WeatherImageFunction.Services;

using WeatherImageFunction.Models;

public interface IWeatherService
{
    Task<List<WeatherStation>> GetWeatherStationsAsync(int maxStations = 50);
    Task<WeatherStation?> GetStationByIdAsync(int stationId);
}