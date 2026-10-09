using KnitErp.Application.Access;

namespace KnitErp.Web.Components.Tools;

/// <summary>Раздел меню: адрес, название и доступен ли он пользователю.</summary>
public sealed record NavEntry(string Href, string Label, bool Allowed);

/// <summary>
/// Что инструмент знает о рабочем месте: кто пользователь, на каком он экране, и как открыть другой инструмент
/// или перейти в раздел. Инструменты не трогают DbContext страницы — у их сервисов свой контекст на операцию.
/// </summary>
public sealed record ToolContext(
    CurrentAccessDto Access,
    string Route,
    string RouteTitle,
    IReadOnlyList<NavEntry> Sections,
    Func<string, string?, Task> OpenTool,
    Func<string, Task> Navigate);
