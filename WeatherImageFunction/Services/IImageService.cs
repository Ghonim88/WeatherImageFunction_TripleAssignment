namespace WeatherImageFunction.Services;

using WeatherImageFunction.Models;

public interface IImageService
{
    Task<byte[]> GetImageAsync(string searchKeyword);
    Task<byte[]> AddWeatherTextToImageAsync(byte[] imageBytes, WeatherStation station);
    Task<string> ProcessStationImageAsync(WeatherStation station, string searchKeyword);
}