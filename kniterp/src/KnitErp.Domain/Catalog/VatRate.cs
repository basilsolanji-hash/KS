using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;

namespace KnitErp.Domain.Catalog;

public enum VatRateKind : byte
{
    Standard = 1,
    Reduced = 2,
    Zero = 3,

    /// <summary>Без НДС (освобождение, не облагается): процента нет.</summary>
    Exempt = 4,
}

/// <summary>
/// Вид ставки НДС организации (D60): «Основная», «Пониженная 10%», «0%», «Без НДС». Номенклатура ссылается на вид,
/// а процент берётся по дате из истории <see cref="Periods"/>: когда закон меняет ставку, добавляется новый период
/// (например, основная в России — 20% до 31.12.2025, 22% с 01.01.2026), и номенклатуру переписывать не нужно.
/// </summary>
public sealed class VatRate
{
    public const int NameMaxLength = 100;

    private readonly List<VatRatePeriod> _periods = [];

    private VatRate()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public VatRateKind Kind { get; private set; }
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<VatRatePeriod> Periods => _periods;

    public static VatRate Create(long organizationId, string name, VatRateKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new BusinessRuleException("vat.kind", "Неизвестный вид ставки НДС.");
        }

        return new VatRate { OrganizationId = organizationId, Name = DomainText.Require(name, NameMaxLength, "Наименование"), Kind = kind };
    }

    /// <summary>Процент с даты. У «Без НДС» процента нет; у «0%» — только 0.</summary>
    public VatRatePeriod AddPeriod(DateOnly validFrom, decimal percent)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("vat.archived", "Ставка в архиве.");
        }

        if (Kind == VatRateKind.Exempt)
        {
            throw new BusinessRuleException("vat.exempt_no_percent", "У вида «Без НДС» процента нет.");
        }

        if (percent is < 0 or > 100 || decimal.Round(percent, 2) != percent)
        {
            throw new BusinessRuleException("vat.percent", "Процент — от 0 до 100, не больше двух знаков после запятой.");
        }

        if (Kind == VatRateKind.Zero && percent != 0)
        {
            throw new BusinessRuleException("vat.zero", "У вида «0%» процент всегда 0.");
        }

        if (_periods.Any(p => p.ValidFrom == validFrom))
        {
            throw new BusinessRuleException("vat.period_exists", $"С {validFrom:dd.MM.yyyy} процент уже задан.");
        }

        var period = new VatRatePeriod(validFrom, percent);
        _periods.Add(period);
        return period;
    }

    /// <summary>Процент на дату или null («Без НДС» или дата раньше первого периода).</summary>
    public decimal? PercentOn(DateOnly date) =>
        Kind == VatRateKind.Exempt ? null : _periods.Where(p => p.ValidFrom <= date).MaxBy(p => p.ValidFrom)?.Percent;

    public void Archive()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("vat.archived", "Ставка уже в архиве.");
        }

        IsArchived = true;
    }

    public void Restore() => IsArchived = false;

    public static string KindName(VatRateKind kind) => kind switch
    {
        VatRateKind.Standard => "Основная",
        VatRateKind.Reduced => "Пониженная",
        VatRateKind.Zero => "0%",
        VatRateKind.Exempt => "Без НДС",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Ставки страны на 09.10.2026 для новой организации (источники — в D61). Бухгалтер проверяет и дополняет их
    /// в справочнике: льготные перечни товаров у каждой страны свои.
    /// </summary>
    public static IReadOnlyList<VatRate> DefaultsFor(long organizationId, string countryCode)
    {
        VatRate Rate(string name, VatRateKind kind, params (int Year, int Month, int Day, decimal Percent)[] periods)
        {
            var r = Create(organizationId, name, kind);
            foreach (var p in periods)
            {
                r.AddPeriod(new DateOnly(p.Year, p.Month, p.Day), p.Percent);
            }

            return r;
        }

        var common = new[] { Rate("0%", VatRateKind.Zero, (2000, 1, 1, 0m)), Rate("Без НДС", VatRateKind.Exempt) };
        IEnumerable<VatRate> specific = countryCode switch
        {
            // Россия: 22% с 01.01.2026 (Федеральный закон от 28.11.2025 № 425-ФЗ), до этого 20%; льготная 10%.
            Countries.Russia =>
            [
                Rate("Основная", VatRateKind.Standard, (2019, 1, 1, 20m), (2026, 1, 1, 22m)),
                Rate("Пониженная 10%", VatRateKind.Reduced, (2004, 1, 1, 10m)),
            ],
            // Узбекистан: 12% с 01.01.2023.
            Countries.Uzbekistan => [Rate("Основная", VatRateKind.Standard, (2023, 1, 1, 12m))],
            // Казахстан: 16% с 01.01.2026 (Налоговый кодекс от 18.07.2025 № 214-VIII), до этого 12%;
            // лекарства и медицинские изделия — 5% в 2026 году, 10% с 2027 года; периодические издания — 10%.
            Countries.Kazakhstan =>
            [
                Rate("Основная", VatRateKind.Standard, (2009, 1, 1, 12m), (2026, 1, 1, 16m)),
                Rate("Лекарства и медизделия", VatRateKind.Reduced, (2026, 1, 1, 5m), (2027, 1, 1, 10m)),
            ],
            // Беларусь: 20%, льготная 10% (отдельные продовольственные товары, лекарства).
            Countries.Belarus =>
            [
                Rate("Основная", VatRateKind.Standard, (2004, 1, 1, 20m)),
                Rate("Пониженная 10%", VatRateKind.Reduced, (2004, 1, 1, 10m)),
            ],
            _ => [],
        };
        return [.. specific, .. common];
    }
}

/// <summary>Процент ставки НДС, действующий с даты до начала следующего периода.</summary>
public sealed class VatRatePeriod
{
    private VatRatePeriod()
    {
    }

    internal VatRatePeriod(DateOnly validFrom, decimal percent)
    {
        ValidFrom = validFrom;
        Percent = percent;
    }

    public long Id { get; private set; }
    public long VatRateId { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public decimal Percent { get; private set; }
}
