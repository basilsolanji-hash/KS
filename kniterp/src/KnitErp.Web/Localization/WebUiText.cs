using KnitErp.Application.Workspace;

namespace KnitErp.Web.Localization;

/// <summary>Переводы для сервисов Application (справка, быстрый поиск, ИИ-помощник) — на языке текущего запроса.</summary>
public sealed class WebUiText : IUiText
{
    public string LanguageCode => Text.CurrentCode;

    public string Translate(string russian) => Text.Message(russian);
}
