using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KnitErp.Application.Common;
using KnitErp.Application.Workspace;

namespace KnitErp.Infrastructure.External;

/// <summary>
/// Погода из Open-Meteo (без ключа). Запрос идёт с сервера с таймаутом 4 секунды; ответ кэшируется на 15 минут
/// на город, чтобы виджет не нагружал сервис и не замедлял страницу. Ошибка сервиса — не ошибка системы.
/// </summary>
public sealed class OpenMeteoWeatherProvider(HttpClient http, IClock clock) : IWeatherProvider
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, (DateTime At, WeatherDto? Value)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<WeatherDto?> GetAsync(string city, CancellationToken ct = default)
    {
        var name = city.Trim();
        if (name.Length is 0 or > 100)
        {
            return null;
        }

        if (Cache.TryGetValue(name, out var hit) && clock.UtcNow - hit.At < CacheFor)
        {
            return hit.Value;
        }

        try
        {
            var geo = await http.GetFromJsonAsync<GeoResponse>(
                $"https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&format=json&name={Uri.EscapeDataString(name)}", ct);
            var place = geo?.Results?.FirstOrDefault();
            if (place is null)
            {
                Cache[name] = (clock.UtcNow, null);
                return null;
            }

            var lat = place.Latitude.ToString(CultureInfo.InvariantCulture);
            var lon = place.Longitude.ToString(CultureInfo.InvariantCulture);
            var forecast = await http.GetFromJsonAsync<ForecastResponse>(
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,wind_speed_10m&wind_speed_unit=ms",
                ct);
            if (forecast?.Current is not { } now)
            {
                throw new ExternalServiceUnavailableException("Сервис погоды вернул пустой ответ.");
            }

            var result = new WeatherDto(place.Name ?? name, Math.Round(now.Temperature, 1), Describe(now.WeatherCode), Math.Round(now.Wind, 1), clock.UtcNow);
            Cache[name] = (clock.UtcNow, result);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new ExternalServiceUnavailableException("Сервис погоды сейчас недоступен.");
        }
    }

    /// <summary>Коды погоды WMO, которые использует Open-Meteo.</summary>
    private static string Describe(int code) => code switch
    {
        0 => "Ясно",
        1 or 2 => "Переменная облачность",
        3 => "Пасмурно",
        45 or 48 => "Туман",
        51 or 53 or 55 or 56 or 57 => "Морось",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "Дождь",
        71 or 73 or 75 or 77 or 85 or 86 => "Снег",
        95 or 96 or 99 => "Гроза",
        _ => "Без осадков",
    };

    private sealed record GeoResponse([property: JsonPropertyName("results")] List<GeoPlace>? Results);

    private sealed record GeoPlace(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude);

    private sealed record ForecastResponse([property: JsonPropertyName("current")] CurrentWeather? Current);

    private sealed record CurrentWeather(
        [property: JsonPropertyName("temperature_2m")] decimal Temperature,
        [property: JsonPropertyName("weather_code")] int WeatherCode,
        [property: JsonPropertyName("wind_speed_10m")] decimal Wind);
}
