using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;

namespace KnitErp.Domain.Catalog;

/// <summary>
/// Контрагент: поставщик пряжи и фурнитуры, покупатель продукции. ИНН необязателен (физлицо без ИНН, иностранная фирма),
/// но если указан — проверяется контрольная сумма. КПП — только у юрлица. Не удаляется — архивируется.
/// </summary>
public sealed class Counterparty
{
    public const int NameMaxLength = 300;
    public const int CommentMaxLength = 1000;

    private Counterparty()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Inn { get; private set; }
    public string? Kpp { get; private set; }
    public bool IsSupplier { get; private set; }
    public bool IsCustomer { get; private set; }
    public string? Comment { get; private set; }
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Counterparty Create(long organizationId, string name, string? inn, string? kpp, bool isSupplier, bool isCustomer, string? comment)
    {
        var c = new Counterparty { OrganizationId = organizationId };
        c.Set(name, inn, kpp, isSupplier, isCustomer, comment);
        return c;
    }

    public IReadOnlyList<FieldChange> Update(string name, string? inn, string? kpp, bool isSupplier, bool isCustomer, string? comment)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Контрагент в архиве. Сначала верните его из архива.");
        }

        var before = (Name, Inn, Kpp, Roles: RolesText(IsSupplier, IsCustomer), Comment);
        Set(name, inn, kpp, isSupplier, isCustomer, comment);
        var changes = new List<FieldChange>();
        void Add(string field, string? b, string? a)
        {
            if (b != a)
            {
                changes.Add(new FieldChange(field, b, a));
            }
        }

        Add("Наименование", before.Name, Name);
        Add("ИНН", before.Inn, Inn);
        Add("КПП", before.Kpp, Kpp);
        Add("Роль", before.Roles, RolesText(IsSupplier, IsCustomer));
        Add("Комментарий", before.Comment, Comment);
        return changes;
    }

    public void Archive()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Контрагент уже в архиве.");
        }

        IsArchived = true;
    }

    public void Restore()
    {
        if (!IsArchived)
        {
            throw new BusinessRuleException("catalog.not_archived", "Контрагент не в архиве.");
        }

        IsArchived = false;
    }

    public static string RolesText(bool isSupplier, bool isCustomer) => (isSupplier, isCustomer) switch
    {
        (true, true) => "Поставщик и покупатель",
        (true, false) => "Поставщик",
        (false, true) => "Покупатель",
        _ => "—",
    };

    private void Set(string name, string? inn, string? kpp, bool isSupplier, bool isCustomer, string? comment)
    {
        if (!isSupplier && !isCustomer)
        {
            throw new BusinessRuleException("catalog.counterparty.role", "Отметьте, поставщик это или покупатель (или оба).");
        }

        var i = string.IsNullOrWhiteSpace(inn) ? null : inn.Trim();
        if (i is not null && !RussianRequisites.IsValidLegalEntityInn(i) && !RussianRequisites.IsValidPersonInn(i))
        {
            throw new BusinessRuleException("catalog.counterparty.inn", "ИНН указан неверно: 10 цифр у организации, 12 у ИП, с верной контрольной суммой.");
        }

        var k = string.IsNullOrWhiteSpace(kpp) ? null : kpp.Trim().ToUpperInvariant();
        if (k is not null && (i is null || i.Length != 10 || !RussianRequisites.IsValidKpp(k)))
        {
            throw new BusinessRuleException("catalog.counterparty.kpp", "КПП указывается только у организации (ИНН из 10 цифр): 9 символов.");
        }

        Name = DomainText.Require(name, NameMaxLength, "Наименование");
        Inn = i;
        Kpp = k;
        IsSupplier = isSupplier;
        IsCustomer = isCustomer;
        Comment = DomainText.Optional(comment, CommentMaxLength, "Комментарий");
    }
}
