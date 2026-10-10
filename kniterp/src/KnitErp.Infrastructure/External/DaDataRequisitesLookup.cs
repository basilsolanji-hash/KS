using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KnitErp.Application.Common;
using KnitErp.Application.Workspace;

namespace KnitErp.Infrastructure.External;

/// <summary>
/// Реквизиты по ИНН из DaData (данные ЕГРЮЛ/ЕГРИП, метод findById/party). Ключ — только в секретах сервера
/// (<c>Requisites__ApiKey</c>); без ключа сервис не настроен. Таймаут короткий, ошибка сервиса — не ошибка системы.
/// </summary>
public sealed class DaDataRequisitesLookup(HttpClient http, RequisitesLookupOptions options) : IRequisitesLookup
{
    private const string Url = "https://suggestions.dadata.ru/suggestions/api/4_1/rs/findById/party";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<CompanyRequisites?> FindByInnAsync(string inn, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            // Головная организация, а не филиал: реквизиты для документов.
            Content = JsonContent.Create(new { query = inn, branch_type = "MAIN" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalServiceUnavailableException($"DaData ответил {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<Response>(ct);
            var d = body?.Suggestions?.FirstOrDefault()?.Data;
            if (d is null || string.IsNullOrEmpty(d.Inn))
            {
                return null;
            }

            var sole = string.Equals(d.Type, "INDIVIDUAL", StringComparison.OrdinalIgnoreCase);
            var fio = d.Fio is { } f ? string.Join(' ', new[] { f.Surname, f.Name, f.Patronymic }.Where(x => !string.IsNullOrWhiteSpace(x))) : null;
            var full = d.Name?.FullWithOpf ?? d.Name?.Full ?? inn;
            var shortName = d.Name?.ShortWithOpf ?? d.Name?.Short ?? full;
            return new CompanyRequisites(sole, full, shortName, d.Inn, sole ? null : d.Kpp, d.Ogrn, d.Address?.UnrestrictedValue ?? d.Address?.Value,
                sole ? null : d.Management?.Post, sole ? (string.IsNullOrWhiteSpace(fio) ? null : fio) : d.Management?.Name,
                d.State?.Status is null or "ACTIVE", d.State?.Status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new ExternalServiceUnavailableException("Справочник реквизитов сейчас недоступен.");
        }
    }

    private sealed record Response([property: JsonPropertyName("suggestions")] List<Suggestion>? Suggestions);

    private sealed record Suggestion([property: JsonPropertyName("data")] Party? Data);

    private sealed record Party(
        [property: JsonPropertyName("inn")] string? Inn,
        [property: JsonPropertyName("kpp")] string? Kpp,
        [property: JsonPropertyName("ogrn")] string? Ogrn,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("name")] PartyName? Name,
        [property: JsonPropertyName("fio")] Fio? Fio,
        [property: JsonPropertyName("management")] Management? Management,
        [property: JsonPropertyName("address")] Address? Address,
        [property: JsonPropertyName("state")] State? State);

    private sealed record PartyName(
        [property: JsonPropertyName("full_with_opf")] string? FullWithOpf,
        [property: JsonPropertyName("short_with_opf")] string? ShortWithOpf,
        [property: JsonPropertyName("full")] string? Full,
        [property: JsonPropertyName("short")] string? Short);

    private sealed record Fio(
        [property: JsonPropertyName("surname")] string? Surname,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("patronymic")] string? Patronymic);

    private sealed record Management([property: JsonPropertyName("name")] string? Name, [property: JsonPropertyName("post")] string? Post);

    private sealed record Address(
        [property: JsonPropertyName("value")] string? Value,
        [property: JsonPropertyName("unrestricted_value")] string? UnrestrictedValue);

    private sealed record State([property: JsonPropertyName("status")] string? Status);
}

/// <summary>Ключ сервиса реквизитов из секретов сервера.</summary>
public sealed record RequisitesLookupOptions(string? ApiKey);
