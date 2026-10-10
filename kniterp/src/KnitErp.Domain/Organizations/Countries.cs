namespace KnitErp.Domain.Organizations;

/// <summary>Страна регистрации организации или контрагента: валюта, налоговый номер, часовой пояс по умолчанию (D60, D61).</summary>
public sealed record CountryInfo(
    string Code, string Name, string CurrencyCode, string CurrencyName, string TaxIdName, string DefaultTimeZone, bool HasKpp);

/// <summary>
/// Страны, в которых работают фабрики (D60). Налоговые номера: Россия — ИНН (10 цифр у организации, 12 у ИП) и КПП;
/// Узбекистан — СТИР (9 цифр), у физлица ПИНФЛ (14 цифр); Казахстан — БИН у организации, ИИН у ИП (12 цифр
/// с контрольной цифрой); Беларусь — УНП (9 цифр). Контрольная сумма проверяется для России и Казахстана;
/// для Узбекистана и Беларуси — только формат (допущение D61).
/// </summary>
public static class Countries
{
    public const string Russia = "RU";
    public const string Uzbekistan = "UZ";
    public const string Kazakhstan = "KZ";
    public const string Belarus = "BY";

    public static readonly IReadOnlyList<CountryInfo> All =
    [
        new(Russia, "Россия", "RUB", "российский рубль", "ИНН", "Europe/Moscow", HasKpp: true),
        new(Uzbekistan, "Узбекистан", "UZS", "узбекский сум", "СТИР", "Asia/Tashkent", HasKpp: false),
        new(Kazakhstan, "Казахстан", "KZT", "казахстанский тенге", "БИН/ИИН", "Asia/Almaty", HasKpp: false),
        new(Belarus, "Беларусь", "BYN", "белорусский рубль", "УНП", "Europe/Minsk", HasKpp: false),
    ];

    public static CountryInfo? Find(string? code) =>
        All.FirstOrDefault(c => string.Equals(c.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Страна по коду («KZ») или названию («Казахстан») — для загрузки из Excel.</summary>
    public static CountryInfo? Parse(string? text) =>
        Find(text) ?? All.FirstOrDefault(c => string.Equals(c.Name, text?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static CountryInfo Get(string? code) =>
        Find(code) ?? throw new Common.BusinessRuleException("country.unknown",
            $"Неизвестная страна «{code}». Допустимые: {string.Join(", ", All.Select(c => $"{c.Code} — {c.Name}"))}.");

    /// <summary>Налоговый номер организации (юридического лица).</summary>
    public static bool IsValidOrganizationTaxId(string country, string? id) => country switch
    {
        Russia => RussianRequisites.IsValidLegalEntityInn(id),
        Uzbekistan or Belarus => IsDigits(id, 9),
        Kazakhstan => IsValidKazakhNumber(id),
        _ => false,
    };

    /// <summary>Налоговый номер контрагента: организации или предпринимателя (физлица).</summary>
    public static bool IsValidCounterpartyTaxId(string country, string? id) => country switch
    {
        Russia => RussianRequisites.IsValidLegalEntityInn(id) || RussianRequisites.IsValidPersonInn(id),
        Uzbekistan => IsDigits(id, 9) || IsDigits(id, 14),
        Kazakhstan => IsValidKazakhNumber(id),
        Belarus => IsDigits(id, 9),
        _ => false,
    };

    /// <summary>Подсказка к ошибке: какой номер ожидается.</summary>
    public static string TaxIdRule(string country, bool organization) => country switch
    {
        Russia => organization ? "ИНН организации: 10 цифр с верной контрольной суммой" : "ИНН: 10 цифр у организации, 12 у ИП, с верной контрольной суммой",
        Uzbekistan => organization ? "СТИР: 9 цифр" : "СТИР: 9 цифр, у физлица ПИНФЛ: 14 цифр",
        Kazakhstan => "БИН/ИИН: 12 цифр с верной контрольной цифрой",
        Belarus => "УНП: 9 цифр",
        _ => "налоговый номер",
    };

    /// <summary>
    /// БИН и ИИН Казахстана: 12 цифр, последняя — контрольная. Веса 1–11; если остаток 10 — веса 3–11, 1, 2;
    /// если снова 10 — номер недействителен.
    /// </summary>
    public static bool IsValidKazakhNumber(string? id)
    {
        if (!IsDigits(id, 12))
        {
            return false;
        }

        static int Sum(string s, int[] weights)
        {
            var sum = 0;
            for (var i = 0; i < 11; i++)
            {
                sum += (s[i] - '0') * weights[i];
            }

            return sum % 11;
        }

        var check = Sum(id!, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
        if (check == 10)
        {
            check = Sum(id!, [3, 4, 5, 6, 7, 8, 9, 10, 11, 1, 2]);
        }

        return check != 10 && check == id![11] - '0';
    }

    private static bool IsDigits(string? s, int length) => s is not null && s.Length == length && s.All(char.IsAsciiDigit);
}
