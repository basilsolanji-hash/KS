namespace KnitErp.Application.Workspace;

/// <summary>Как открывается инструмент: в боковой панели поверх страницы или ссылкой в новой вкладке.</summary>
public enum ToolOpenMode
{
    Panel,
    NewTab,
}

/// <summary>Инструмент панели быстрого доступа. Значок рисует интерфейс по Id.</summary>
public sealed record ToolDefinition(string Id, string Title, string Hint, ToolOpenMode Mode);

/// <summary>
/// Перечень инструментов. Календарь и задачи — один модуль с общими данными (разные виды), уведомления собирают
/// события существующих модулей, поиск ищет по разделам, номенклатуре и документам — ничего не дублируется.
/// </summary>
public static class ToolCatalog
{
    public const string Search = "search";
    public const string Calculator = "calculator";
    public const string Calendar = "calendar";
    public const string Tasks = "tasks";
    public const string Notes = "notes";
    public const string Notifications = "notifications";
    public const string Favorites = "favorites";
    public const string Links = "links";
    public const string Site = "site";
    public const string Assistant = "assistant";
    public const string Weather = "weather";
    public const string Support = "support";
    public const string Help = "help";

    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new(Search, "Быстрый поиск", "Разделы, номенклатура, документы и справка по коду или названию", ToolOpenMode.Panel),
        new(Notifications, "Уведомления", "Напоминания, документы на утверждение, ответы поддержки", ToolOpenMode.Panel),
        new(Calculator, "Калькулятор", "Быстрые расчёты и проценты, результат можно скопировать", ToolOpenMode.Panel),
        new(Calendar, "Календарь", "Личные события и напоминания по датам", ToolOpenMode.Panel),
        new(Tasks, "Задачи", "Личный список дел; задачи с датой видны в календаре", ToolOpenMode.Panel),
        new(Notes, "Заметки", "Личные заметки — видны только вам", ToolOpenMode.Panel),
        new(Favorites, "Избранное", "Закреплённые разделы системы", ToolOpenMode.Panel),
        new(Links, "Мои ссылки", "Свои ссылки на нужные сайты и документы", ToolOpenMode.Panel),
        new(Site, "Сайт фабрики", "Открыть сайт организации в новой вкладке", ToolOpenMode.NewTab),
        new(Assistant, "ИИ-помощник", "Вопросы по работе в системе и текущему разделу", ToolOpenMode.Panel),
        new(Weather, "Погода", "Погода в выбранном городе", ToolOpenMode.Panel),
        new(Support, "Поддержка", "Создать обращение и следить за его статусом", ToolOpenMode.Panel),
        new(Help, "Справка и обучение", "Инструкции, поиск по справке и подсказки по текущему экрану", ToolOpenMode.Panel),
    ];

    /// <summary>Панель нового пользователя: самое полезное на каждый день.</summary>
    public static readonly IReadOnlyList<string> DefaultPinned = [Search, Notifications, Calculator, Calendar, Help];

    public const int DefaultVisible = 4;
    public const int MaxPinned = 13;
    public const int MaxVisible = 8;

    public static ToolDefinition? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
