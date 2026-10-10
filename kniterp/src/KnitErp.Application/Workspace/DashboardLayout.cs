using System.Text.Json;

namespace KnitErp.Application.Workspace;

/// <summary>
/// Личная настройка главного экрана: порядок блоков и скрытые блоки. Хранится в личных данных пользователя
/// (<see cref="PersonalToolsService"/>), у каждого пользователя в каждой организации своя.
/// </summary>
public sealed record DashboardLayout(IReadOnlyList<string> Order, IReadOnlyList<string> Hidden)
{
    public bool Shows(string block) => !Hidden.Contains(block);

    /// <summary>Видимые блоки основной части экрана в порядке пользователя.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Visible => Order.Where(Shows);
}

/// <summary>Блоки главного экрана. Названия для экрана — в DashboardPage (переводятся там).</summary>
public static class DashboardBlocks
{
    public const string Kpi = "kpi";

    /// <summary>Деньги на счетах и в кассах всех юрлиц (D80).</summary>
    public const string Money = "money";
    public const string Attention = "attention";
    public const string Debtors = "debtors";
    public const string Charts = "charts";
    public const string Recent = "recent";
    public const string Month = "month";
    public const string Reference = "reference";

    /// <summary>«Готовность к запуску» — в шапке экрана: её можно скрыть, но не переставить.</summary>
    public const string Readiness = "readiness";

    /// <summary>Порядок по умолчанию: сначала деньги и то, что ждёт действия, — они помещаются на первый экран.</summary>
    public static readonly IReadOnlyList<string> DefaultOrder = [Kpi, Money, Attention, Debtors, Charts, Recent, Month, Reference];

    public static readonly IReadOnlyList<string> All = [.. DefaultOrder, Readiness];

    public static DashboardLayout Default { get; } = new(DefaultOrder, []);

    /// <summary>
    /// Неизвестные и повторные блоки отбрасываются; блоки, которых нет в сохранённом порядке (например, новые), встают в конец.
    /// </summary>
    public static DashboardLayout Clean(DashboardLayout? layout)
    {
        if (layout is null)
        {
            return Default;
        }

        var order = (layout.Order ?? []).Where(DefaultOrder.Contains).Distinct().ToList();
        order.AddRange(DefaultOrder.Where(b => !order.Contains(b)));
        var hidden = (layout.Hidden ?? []).Where(All.Contains).Distinct().OrderBy(b => All.IndexOf(b)).ToList();
        return new DashboardLayout(order, hidden);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(DashboardLayout layout) => JsonSerializer.Serialize(Clean(layout), Json);

    /// <summary>Повреждённая или пустая запись — настройка по умолчанию: экран не должен ломаться.</summary>
    public static DashboardLayout Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            return Clean(JsonSerializer.Deserialize<DashboardLayout>(json, Json));
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    private static int IndexOf(this IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
