namespace KnitErp.Application.Workspace;

/// <summary>Внешний сервис не ответил или выключен. Основная работа системы от этого не зависит.</summary>
public sealed class ExternalServiceUnavailableException(string message) : Exception(message);

public sealed record WeatherDto(string City, decimal TemperatureC, string Description, decimal WindMs, DateTime ObservedAtUtc);

/// <summary>Погода по городу. Реализация — в Infrastructure, с коротким таймаутом и кэшем.</summary>
public interface IWeatherProvider
{
    /// <summary>null — город не найден; при недоступности сервиса — <see cref="ExternalServiceUnavailableException"/>.</summary>
    Task<WeatherDto?> GetAsync(string city, CancellationToken ct = default);
}

public sealed record AssistantTurn(bool FromUser, string Text);

/// <summary>Языковая модель ИИ-помощника. Если ключ не задан, помощник отвечает по справочному центру.</summary>
public interface IAssistantModel
{
    bool IsConfigured { get; }

    Task<string> AskAsync(string systemPrompt, IReadOnlyList<AssistantTurn> conversation, CancellationToken ct = default);
}

/// <summary>Помощник без внешней модели: всегда «не настроен».</summary>
public sealed class DisabledAssistantModel : IAssistantModel
{
    public bool IsConfigured => false;

    public Task<string> AskAsync(string systemPrompt, IReadOnlyList<AssistantTurn> conversation, CancellationToken ct = default) =>
        throw new ExternalServiceUnavailableException("ИИ-помощник не подключён.");
}
