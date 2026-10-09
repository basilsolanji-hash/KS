using KnitErp.Domain.Common;

namespace KnitErp.Domain.Organizations;

/// <summary>
/// Юридическая организация — граница данных. Все бизнес-записи принадлежат одной организации (ТЗ §4.6 п.3, ORG 01).
/// </summary>
public sealed class Organization
{
    public const int NameMaxLength = 300;
    public const int ShortNameMaxLength = 150;
    public const int AddressMaxLength = 500;
    public const int TimeZoneMaxLength = 64;
    public const int WebsiteMaxLength = 300;

    private Organization()
    {
    }

    public long Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string ShortName { get; private set; } = string.Empty;
    public string Inn { get; private set; } = string.Empty;

    /// <summary>КПП. До сверки по документу организации не выводится в печатные формы (вопрос D15).</summary>
    public string? Kpp { get; private set; }

    public bool KppVerified { get; private set; }
    public string? ActualAddress { get; private set; }

    /// <summary>Сайт фабрики для ярлыка на панели быстрого доступа. Только http(s).</summary>
    public string? WebsiteUrl { get; private set; }

    /// <summary>Часовой пояс учёта (IANA). Время хранится в UTC, показывается в этом поясе.</summary>
    public string TimeZoneId { get; private set; } = "Europe/Moscow";

    public string CurrencyCode { get; private set; } = "RUB";
    public bool IsArchived { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>КПП, который можно печатать в юридических формах, или null.</summary>
    public string? PrintableKpp => KppVerified ? Kpp : null;

    public static Organization Create(
        string fullName,
        string shortName,
        string inn,
        string? kpp,
        bool kppVerified,
        string timeZoneId,
        DateTime nowUtc)
    {
        var org = new Organization
        {
            FullName = RequireText(fullName, NameMaxLength, "Полное наименование"),
            ShortName = RequireText(shortName, ShortNameMaxLength, "Сокращённое наименование"),
            Inn = inn?.Trim() ?? string.Empty,
            CreatedAtUtc = nowUtc,
        };

        if (!RussianRequisites.IsValidLegalEntityInn(org.Inn))
        {
            throw new BusinessRuleException("org.inn.invalid", "ИНН юридического лица указан неверно: 10 цифр с верной контрольной суммой.");
        }

        org.SetKpp(kpp, kppVerified);
        org.SetTimeZone(timeZoneId);
        return org;
    }

    /// <summary>Меняет изменяемые реквизиты и возвращает список изменений для журнала аудита.</summary>
    public IReadOnlyList<FieldChange> UpdateRequisites(string? actualAddress, string timeZoneId, string? websiteUrl = null)
    {
        EnsureNotArchived();
        var changes = new List<FieldChange>();

        var address = string.IsNullOrWhiteSpace(actualAddress) ? null : actualAddress.Trim();
        if (address is { Length: > AddressMaxLength })
        {
            throw new BusinessRuleException("org.address.too_long", $"Адрес длиннее {AddressMaxLength} символов.");
        }

        if (address != ActualAddress)
        {
            changes.Add(new FieldChange("ActualAddress", ActualAddress, address));
            ActualAddress = address;
        }

        if (timeZoneId != TimeZoneId)
        {
            var before = TimeZoneId;
            SetTimeZone(timeZoneId);
            changes.Add(new FieldChange("TimeZoneId", before, TimeZoneId));
        }

        var website = NormalizeWebsite(websiteUrl);
        if (website != WebsiteUrl)
        {
            changes.Add(new FieldChange("WebsiteUrl", WebsiteUrl, website));
            WebsiteUrl = website;
        }

        return changes;
    }

    public FieldChange? ConfirmKpp(string kpp)
    {
        EnsureNotArchived();
        var before = $"{Kpp} (сверен: {KppVerified})";
        SetKpp(kpp, verified: true);
        return new FieldChange("Kpp", before, $"{Kpp} (сверен: True)");
    }

    /// <summary>Адрес сайта: пусто — нет сайта; без схемы дописывается https://; только http и https.</summary>
    public static string? NormalizeWebsite(string? url)
    {
        var value = url?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (value.Length > WebsiteMaxLength || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || string.IsNullOrEmpty(uri.Host))
        {
            throw new BusinessRuleException("org.website.invalid", "Адрес сайта указан неверно, например: https://fabrika.ru");
        }

        return uri.ToString();
    }

    private void SetKpp(string? kpp, bool verified)
    {
        var value = string.IsNullOrWhiteSpace(kpp) ? null : kpp.Trim().ToUpperInvariant();
        if (value is not null && !RussianRequisites.IsValidKpp(value))
        {
            throw new BusinessRuleException("org.kpp.invalid", "КПП указан неверно: 9 символов.");
        }

        Kpp = value;
        KppVerified = value is not null && verified;
    }

    private void SetTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > TimeZoneMaxLength
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            throw new BusinessRuleException("org.timezone.invalid", $"Неизвестный часовой пояс: {timeZoneId}.");
        }

        TimeZoneId = timeZoneId;
    }

    private void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("org.archived", "Организация в архиве, изменения запрещены.");
        }
    }

    private static string RequireText(string? value, int maxLength, string field)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            throw new BusinessRuleException("org.field.required", $"Поле «{field}» обязательно.");
        }

        if (v.Length > maxLength)
        {
            throw new BusinessRuleException("org.field.too_long", $"Поле «{field}» длиннее {maxLength} символов.");
        }

        return v;
    }
}

public sealed record FieldChange(string Field, string? Before, string? After);
