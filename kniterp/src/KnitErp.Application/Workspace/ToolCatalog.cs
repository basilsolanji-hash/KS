namespace KnitErp.Application.Workspace;

/// <summary>
/// Как открывается инструмент: в боковой панели поверх страницы, ссылкой в новой вкладке или отдельным окном,
/// которое остаётся открытым, пока работаете со страницей (калькулятор).
/// </summary>
public enum ToolOpenMode
{
    Panel,
    NewTab,
    Window,
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
        new(Calculator, "Калькулятор", "Отдельное окно, считает с клавиатуры; проценты, копирование результата", ToolOpenMode.Window),
        new(Calendar, "Календарь", "Личные события и напоминания по датам", ToolOpenMode.Panel),
        new(Tasks, "Задачи", "Личный список дел; задачи с датой видны в календаре", ToolOpenMode.Panel),
        new(Notes, "Заметки", "Личные заметки — видны только вам", ToolOpenMode.Panel),
        new(Favorites, "Избранное", "Закреплённые разделы системы", ToolOpenMode.Panel),
        new(Links, "Мои ссылки", "Свои ссылки на нужные сайты и документы", ToolOpenMode.Panel),
        new(Site, "Сайт организации", "Открыть сайт организации в новой вкладке", ToolOpenMode.NewTab),
        new(Assistant, "ИИ-помощник", "Вопросы по работе в системе и текущему разделу", ToolOpenMode.Panel),
        new(Weather, "Погода", "Погода в выбранном городе", ToolOpenMode.Panel),
        new(Support, "Поддержка", "Создать обращение и следить за его статусом", ToolOpenMode.Panel),
        new(Help, "Справка и обучение", "Инструкции, поиск по справке и подсказки по текущему экрану", ToolOpenMode.Panel),
    ];

    /// <summary>
    /// Не закрепляются: быстрый поиск всегда первым в верхней строке, обучение и поддержка — всегда внизу меню слева.
    /// </summary>
    public static readonly IReadOnlyList<string> Fixed = [Search, Support, Help];

    /// <summary>Панель нового пользователя: пять значков на каждый день.</summary>
    public static readonly IReadOnlyList<string> DefaultPinned = [Calculator, Calendar, Tasks, Notes, Notifications];

    /// <summary>Прежняя панель по умолчанию: кто её не менял, получает новую.</summary>
    public static readonly IReadOnlyList<string> LegacyDefaultPinned = [Search, Notifications, Calculator, Calendar, Help];

    public const int DefaultVisible = 5;
    public const int MaxPinned = 13;
    public const int MaxVisible = 8;

    public static ToolDefinition? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>Инструменты, которые можно закрепить: всё, кроме постоянных.</summary>
    public static IEnumerable<ToolDefinition> Configurable => All.Where(t => !Fixed.Contains(t.Id));
}
