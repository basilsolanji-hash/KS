namespace KnitErp.Domain.Access;

/// <summary>Язык интерфейса (D60): код, культура .NET и самоназвание языка для переключателя.</summary>
public sealed record UiLanguage(string Code, string Culture, string NativeName);

/// <summary>
/// Языки интерфейса: русский (основной), узбекский (латиница — официальная письменность), казахский (кириллица),
/// белорусский. Меняется только язык текстов; формат чисел и дат остаётся одинаковым для всех, чтобы ввод
/// количеств не зависел от языка пользователя.
/// </summary>
public static class UiLanguages
{
    public const string Default = "ru";

    public static readonly IReadOnlyList<UiLanguage> All =
    [
        new("ru", "ru-RU", "Русский"),
        new("uz", "uz-Latn-UZ", "Oʻzbekcha"),
        new("kk", "kk-KZ", "Қазақша"),
        new("be", "be-BY", "Беларуская"),
    ];

    public static UiLanguage? Find(string? code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
}
