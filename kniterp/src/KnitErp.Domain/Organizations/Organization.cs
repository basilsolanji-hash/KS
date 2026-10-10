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
    public const int BankNameMaxLength = 300;
    public const int BicMaxLength = 11;
    public const int AccountMaxLength = 34;
    public const int PersonNameMaxLength = 150;
    public const int PositionMaxLength = 100;

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

    /// <summary>Сайт организации для ярлыка на панели быстрого доступа. Только http(s).</summary>
    public string? WebsiteUrl { get; private set; }

    /// <summary>Часовой пояс учёта (IANA). Время хранится в UTC, показывается в этом поясе.</summary>
    public string TimeZoneId { get; private set; } = "Europe/Moscow";

    /// <summary>Страна регистрации (D60): определяет налоговый номер, валюту и ставки НДС. После создания не меняется.</summary>
    public string CountryCode { get; private set; } = Countries.Russia;

    public string CurrencyCode { get; private set; } = "RUB";

    /// <summary>Юридический адрес — для печатных форм (счёт, УПД). Пусто — печатается фактический.</summary>
    public string? LegalAddress { get; private set; }

    public string? BankName { get; private set; }

    /// <summary>БИК (Россия) или код банка / SWIFT другой страны.</summary>
    public string? BankBic { get; private set; }

    /// <summary>Расчётный счёт или IBAN.</summary>
    public string? BankAccount { get; private set; }

    public string? BankCorrAccount { get; private set; }

    /// <summary>Руководитель и главный бухгалтер — подписи в печатных формах («Иванов И. И.»).</summary>
    public string? DirectorName { get; private set; }

    public string? AccountantName { get; private set; }

    /// <summary>Должность руководителя для подписи в УПД («Генеральный директор»).</summary>
    public string? DirectorPosition { get; private set; }

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
        DateTime nowUtc,
        string countryCode = Countries.Russia)
    {
        var country = Countries.Get(countryCode);
        var org = new Organization
        {
            FullName = RequireText(fullName, NameMaxLength, "Полное наименование"),
            ShortName = RequireText(shortName, ShortNameMaxLength, "Сокращённое наименование"),
            Inn = inn?.Trim() ?? string.Empty,
            CountryCode = country.Code,
            CurrencyCode = country.CurrencyCode,
            CreatedAtUtc = nowUtc,
        };

        if (!Countries.IsValidOrganizationTaxId(country.Code, org.Inn))
        {
            throw new BusinessRuleException("org.inn.invalid", $"{Countries.TaxIdRule(country.Code, organization: true)} — номер указан неверно.");
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

    /// <summary>
    /// Реквизиты для печатных форм (D68): юридический адрес, банк, подписи. Для России БИК и счета проверяются
    /// по контрольному ключу; для других стран — только длина и символы. Возвращает изменения для журнала.
    /// </summary>
    public IReadOnlyList<FieldChange> UpdatePrintRequisites(PrintRequisites r)
    {
        EnsureNotArchived();
        var legal = DomainText.Optional(r.LegalAddress, AddressMaxLength, "Юридический адрес");
        var bank = DomainText.Optional(r.BankName, BankNameMaxLength, "Банк");
        var bic = Code(r.BankBic, BicMaxLength, "БИК");
        var account = Code(r.BankAccount, AccountMaxLength, "Расчётный счёт");
        var corr = Code(r.BankCorrAccount, AccountMaxLength, "Корреспондентский счёт");
        var director = DomainText.Optional(r.DirectorName, PersonNameMaxLength, "Руководитель");
        var accountant = DomainText.Optional(r.AccountantName, PersonNameMaxLength, "Главный бухгалтер");
        var position = DomainText.Optional(r.DirectorPosition, PositionMaxLength, "Должность руководителя");

        if (CountryCode == Countries.Russia)
        {
            if (bic is not null && !RussianRequisites.IsValidBik(bic))
            {
                throw new BusinessRuleException("org.bank.bik", "БИК — 9 цифр, начинается с 04.");
            }

            if ((account is not null || corr is not null) && bic is null)
            {
                throw new BusinessRuleException("org.bank.bik_required", "Укажите БИК банка — по нему проверяются счета.");
            }

            if (account is not null && !RussianRequisites.IsValidSettlementAccount(account, bic!))
            {
                throw new BusinessRuleException("org.bank.account", "Расчётный счёт не сходится с БИК: проверьте 20 цифр счёта и БИК.");
            }

            if (corr is not null && !RussianRequisites.IsValidCorrespondentAccount(corr, bic!))
            {
                throw new BusinessRuleException("org.bank.corr", "Корреспондентский счёт не сходится с БИК: проверьте 20 цифр (начинается с 301).");
            }
        }

        var changes = new List<FieldChange>();
        string? Set(string field, string? before, string? after)
        {
            if (before != after)
            {
                changes.Add(new FieldChange(field, before, after));
            }

            return after;
        }

        LegalAddress = Set("LegalAddress", LegalAddress, legal);
        BankName = Set("BankName", BankName, bank);
        BankBic = Set("BankBic", BankBic, bic);
        BankAccount = Set("BankAccount", BankAccount, account);
        BankCorrAccount = Set("BankCorrAccount", BankCorrAccount, corr);
        DirectorName = Set("DirectorName", DirectorName, director);
        AccountantName = Set("AccountantName", AccountantName, accountant);
        DirectorPosition = Set("DirectorPosition", DirectorPosition, position);
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

    /// <summary>Код или номер счёта: без пробелов, только латинские буквы и цифры.</summary>
    private static string? Code(string? value, int maxLength, string field)
    {
        var v = value?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }

        if (v.Length > maxLength || !v.All(char.IsAsciiLetterOrDigit))
        {
            throw new BusinessRuleException("org.bank.code", $"Поле «{field}»: только цифры и латинские буквы, не длиннее {maxLength}.");
        }

        return v;
    }

    private void SetKpp(string? kpp, bool verified)
    {
        var value = string.IsNullOrWhiteSpace(kpp) ? null : kpp.Trim().ToUpperInvariant();
        if (value is not null && !Countries.Get(CountryCode).HasKpp)
        {
            throw new BusinessRuleException("org.kpp.not_applicable", "КПП бывает только у российских организаций.");
        }

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

public sealed record PrintRequisites(
    string? LegalAddress, string? BankName, string? BankBic, string? BankAccount, string? BankCorrAccount, string? DirectorName, string? AccountantName,
    string? DirectorPosition = null);
