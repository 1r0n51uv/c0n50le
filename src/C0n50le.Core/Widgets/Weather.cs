using System.Globalization;
using System.Text.Json;

namespace C0n50le.Core.Widgets;

/// <summary>What the sky is doing, from the WMO weather code Open-Meteo returns.</summary>
public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Showers, Thunderstorm }

public sealed record WeatherPlace(string Name, string Country, double Latitude, double Longitude);

public sealed record WeatherReport(string Place, double Temperature, double Low, double High, WeatherKind Kind, bool IsDay);

/// <summary>Current weather from Open-Meteo: free, without a key. Only the city name typed in Settings leaves the PC.</summary>
public sealed class WeatherClient
{
    private readonly HttpClient _client;

    public WeatherClient(HttpClient client) => _client = client;

    public async Task<WeatherReport?> GetAsync(string city, bool fahrenheit, string language, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(city)) return null;
        var lang = language.Length >= 2 ? language[..2] : "en";
        var search = $"https://geocoding-api.open-meteo.com/v1/search?count=1&format=json&language={lang}&name={Uri.EscapeDataString(city.Trim())}";
        var place = ParsePlace(await _client.GetStringAsync(search, token));
        if (place is null) return null;
        var forecast = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude}&longitude={place.Longitude}")
            + "&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1"
            + (fahrenheit ? "&temperature_unit=fahrenheit" : "");
        return ParseForecast(await _client.GetStringAsync(forecast, token), place.Name);
    }

    public static WeatherPlace? ParsePlace(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;
        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("latitude", out var lat) || !result.TryGetProperty("longitude", out var lon)) continue;
            return new WeatherPlace(Text(result, "name"), Text(result, "country"), lat.GetDouble(), lon.GetDouble());
        }
        return null;
    }

    public static WeatherReport? ParseForecast(string json, string place)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("current", out var current) || !current.TryGetProperty("temperature_2m", out var temperature)) return null;
        var code = current.TryGetProperty("weather_code", out var weatherCode) ? weatherCode.GetInt32() : 0;
        var isDay = !current.TryGetProperty("is_day", out var day) || day.GetInt32() != 0;
        double First(string name, double fallback) =>
            root.TryGetProperty("daily", out var daily) && daily.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            && values.GetArrayLength() > 0 && values[0].ValueKind == JsonValueKind.Number ? values[0].GetDouble() : fallback;
        var now = temperature.GetDouble();
        return new WeatherReport(place, now, First("temperature_2m_min", now), First("temperature_2m_max", now), Kind(code), isDay);
    }

    /// <summary>WMO 4677 codes, grouped the way a weather icon shows them.</summary>
    public static WeatherKind Kind(int code) => code switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        >= 61 and <= 67 => WeatherKind.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherKind.Snow,
        >= 80 and <= 82 => WeatherKind.Showers,
        >= 95 => WeatherKind.Thunderstorm,
        _ => WeatherKind.Cloudy
    };

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
