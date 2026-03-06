using System.ComponentModel;

namespace ToolGate.Samples.AzureChat.Tools;

public static class WeatherTool
{
    [Description("Gets the current weather for a given city")]
    public static string GetWeather(
        [Description("The city name")] string city)
    {
        // Simulated weather data — in production this would call a real weather API.
        var temperatures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Seattle"] = 12,
            ["London"] = 8,
            ["Tokyo"] = 22,
            ["Sydney"] = 26,
        };

        var temp = temperatures.GetValueOrDefault(city, Random.Shared.Next(5, 35));
        return $"The weather in {city} is {temp}°C with partly cloudy skies.";
    }
}
