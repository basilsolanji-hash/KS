using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnitErp.Domain.Access;
using Microsoft.AspNetCore.Localization;

namespace KnitErp.Web.Localization;

/// <summary>
/// Переводы интерфейса (D60). Ключ — русский текст, переводы — в Localization/{uz,kk,be}.json. Чего нет в переводе,
/// показывается по-русски: так непереведённый экран остаётся рабочим, а полноту переводов проверяет тест.
/// Язык берётся из культуры интерфейса запроса (cookie языка; ставится при входе и в меню профиля).
/// </summary>
public static class Text
{
    private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> Translations = Load();

    /// <summary>Текст на языке пользователя.</summary>
    public static string L(string russian)
    {
        var code = CurrentCode;
        return code != UiLanguages.Default && Translations.TryGetValue(code, out var dict) && dict.TryGetValue(russian, out var t)
            ? t
            : russian;
    }

    /// <summary>Текст с подстановками {0}, {1}… — как в string.Format.</summary>
    public static string L(string russian, params object?[] args) => string.Format(CultureInfo.CurrentCulture, L(russian), args);

    /// <summary>
    /// Сообщение сервиса на языке пользователя (D60). Сообщения правил собираются в коде с подстановками
    /// («Позиция «ПР-1» в архиве»), поэтому ищется шаблон перевода «Позиция «{0}» в архиве»; подставленные значения
    /// тоже переводятся, если для них есть перевод. Нет шаблона — сообщение остаётся по-русски.
    /// </summary>
    public static string Message(string russian)
    {
        var code = CurrentCode;
        if (code == UiLanguages.Default || !Translations.TryGetValue(code, out var dict))
        {
            return russian;
        }

        if (dict.TryGetValue(russian, out var exact))
        {
            return exact;
        }

        foreach (var (template, regex) in Templates[code])
        {
            var match = regex.Match(russian);
            if (match.Success)
            {
                var args = new object?[match.Groups.Count - 1];
                for (var i = 0; i < args.Length; i++)
                {
                    args[i] = L(match.Groups["g" + i].Value);
                }

                return string.Format(CultureInfo.CurrentCulture, dict[template], args);
            }
        }

        return russian;
    }

    private static readonly Regex Placeholder = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);

    private static readonly FrozenDictionary<string, (string Template, Regex Pattern)[]> Templates = Translations.ToFrozenDictionary(
        t => t.Key,
        t => t.Value.Keys.Where(k => Placeholder.IsMatch(k)).OrderByDescending(k => k.Length)
            .Select(k => (k, new Regex("^" + Placeholder.Replace(Regex.Escape(k).Replace(@"\{", "{"), m => $"(?<g{m.Groups[1].Value}>.+?)") + "$",
                RegexOptions.Singleline | RegexOptions.CultureInvariant)))
            .ToArray());

    /// <summary>Код языка текущего запроса: ru, uz, kk, be.</summary>
    public static string CurrentCode =>
        UiLanguages.All.FirstOrDefault(l => l.Culture == CultureInfo.CurrentUICulture.Name)?.Code ?? UiLanguages.Default;

    /// <summary>Ключи перевода языка — для проверки полноты.</summary>
    public static IReadOnlyCollection<string> KeysOf(string code) =>
        Translations.TryGetValue(code, out var dict) ? dict.Keys : [];

    /// <summary>Значение cookie языка: формат чисел и дат — всегда русский (ввод количеств не зависит от языка), тексты — выбранного языка.</summary>
    public static string CookieValue(UiLanguage language) =>
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture: "ru-RU", uiCulture: language.Culture));

    public static void SetCookie(HttpContext http, string? code)
    {
        var language = UiLanguages.Find(code) ?? UiLanguages.All[0];
        http.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, CookieValue(language), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });
    }

    private static FrozenDictionary<string, FrozenDictionary<string, string>> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var result = new Dictionary<string, FrozenDictionary<string, string>>();
        foreach (var language in UiLanguages.All.Where(l => l.Code != UiLanguages.Default))
        {
            using var stream = assembly.GetManifestResourceStream($"KnitErp.Web.Localization.{language.Code}.json");
            if (stream is null)
            {
                continue;
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            result[language.Code] = dict.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToFrozenDictionary();
        }

        return result.ToFrozenDictionary();
    }
}
