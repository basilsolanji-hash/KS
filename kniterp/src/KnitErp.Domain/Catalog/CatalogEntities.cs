using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;

namespace KnitErp.Domain.Catalog;

/// <summary>
/// Единица измерения. Точность — сколько знаков после запятой допустимо в количестве
/// (шт — 0, кг — 3). Количества хранятся в decimal(18,6), поэтому точность не больше 6.
/// </summary>
public sealed class UnitOfMeasure
{
    public const int CodeMaxLength = 10;
    public const int NameMaxLength = 50;
    public const int SymbolMaxLength = 10;
    public const byte MaxPrecision = 6;

    private UnitOfMeasure()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }

    /// <summary>Код по ОКЕИ (796 — штука, 166 — килограмм); для своих единиц — любой короткий код.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string Symbol { get; private set; } = string.Empty;
    public byte Precision { get; private set; }
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Стандартный набор для новой организации. Тот же список — в миграции AddCatalogAndWarehouses.</summary>
    public static readonly IReadOnlyList<(string Code, string Name, string Symbol, byte Precision)> Defaults =
    [
        ("796", "Штука", "шт", 0),
        ("166", "Килограмм", "кг", 3),
        ("163", "Грамм", "г", 1),
        ("006", "Метр", "м", 2),
        ("055", "Квадратный метр", "м²", 3),
        ("715", "Пара", "пар", 0),
        ("778", "Упаковка", "упак", 0),
    ];

    public static UnitOfMeasure Create(long organizationId, string code, string name, string symbol, byte precision)
    {
        var unit = new UnitOfMeasure { OrganizationId = organizationId };
        unit.Code = DomainText.Require(code, CodeMaxLength, "Код единицы");
        unit.Set(name, symbol, precision);
        return unit;
    }

    public IReadOnlyList<FieldChange> Update(string name, string symbol, byte precision)
    {
        EnsureActive();
        var before = (Name, Symbol, Precision);
        Set(name, symbol, precision);
        var changes = new List<FieldChange>();
        if (before.Name != Name)
        {
            changes.Add(new FieldChange("Наименование", before.Name, Name));
        }

        if (before.Symbol != Symbol)
        {
            changes.Add(new FieldChange("Обозначение", before.Symbol, Symbol));
        }

        if (before.Precision != Precision)
        {
            changes.Add(new FieldChange("Знаков после запятой", before.Precision.ToString(), Precision.ToString()));
        }

        return changes;
    }

    public void Archive(int activeItems)
    {
        EnsureActive();
        if (activeItems > 0)
        {
            throw new BusinessRuleException("catalog.unit.in_use", $"Единицу используют позиции номенклатуры ({activeItems}).");
        }

        IsArchived = true;
    }

    private void Set(string name, string symbol, byte precision)
    {
        Name = DomainText.Require(name, NameMaxLength, "Наименование единицы");
        Symbol = DomainText.Require(symbol, SymbolMaxLength, "Обозначение единицы");
        if (precision > MaxPrecision)
        {
            throw new BusinessRuleException("catalog.unit.precision", $"Знаков после запятой — от 0 до {MaxPrecision}.");
        }

        Precision = precision;
    }

    private void EnsureActive()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Запись в архиве.");
        }
    }
}

/// <summary>Тип номенклатуры. Допущение D32: фиксированный список; числа хранятся в базе и закреплены CHECK.</summary>
public enum ItemType : byte
{
    RawMaterial = 1,
    Material = 2,
    Accessory = 3,
    SemiFinished = 4,
    Finished = 5,
    Packaging = 6,
    Other = 9,
}

public static class ItemTypes
{
    public static readonly IReadOnlyList<ItemType> All =
        [ItemType.RawMaterial, ItemType.Material, ItemType.Accessory, ItemType.SemiFinished, ItemType.Finished, ItemType.Packaging, ItemType.Other];

    public static string Name(ItemType type) => type switch
    {
        ItemType.RawMaterial => "Пряжа и сырьё",
        ItemType.Material => "Материалы",
        ItemType.Accessory => "Фурнитура",
        ItemType.SemiFinished => "Полуфабрикаты",
        ItemType.Finished => "Готовая продукция",
        ItemType.Packaging => "Упаковка",
        ItemType.Other => "Прочее",
        _ => type.ToString(),
    };

    public static bool IsDefined(ItemType type) => All.Contains(type);
}

/// <summary>
/// Позиция номенклатуры: пряжа, фурнитура, изделие. Код уникален в организации навсегда — архивный код
/// не выдаётся повторно, чтобы старые документы не указывали на другую вещь. Не удаляется — архивируется.
/// </summary>
public sealed class Item
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 300;
    public const int DescriptionMaxLength = 1000;

    private Item()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public ItemType Type { get; private set; }
    public long UnitId { get; private set; }
    public string? Description { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Item Create(long organizationId, string code, string name, ItemType type, long unitId, string? description, DateTime nowUtc)
    {
        var item = new Item { OrganizationId = organizationId, CreatedAtUtc = nowUtc };
        item.Set(code, name, type, unitId, description);
        return item;
    }

    public IReadOnlyList<FieldChange> Update(string code, string name, ItemType type, long unitId, string? description)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Позиция в архиве. Сначала верните её из архива.");
        }

        var before = (Code, Name, Type, UnitId, Description);
        Set(code, name, type, unitId, description);
        var changes = new List<FieldChange>();
        void Add(string field, string? b, string? a)
        {
            if (b != a)
            {
                changes.Add(new FieldChange(field, b, a));
            }
        }

        Add("Код", before.Code, Code);
        Add("Наименование", before.Name, Name);
        Add("Тип", ItemTypes.Name(before.Type), ItemTypes.Name(Type));
        Add(nameof(UnitId), before.UnitId.ToString(), UnitId.ToString());
        Add("Описание", before.Description, Description);
        return changes;
    }

    public void Archive(DateTime nowUtc)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Позиция уже в архиве.");
        }

        IsArchived = true;
        ArchivedAtUtc = nowUtc;
    }

    public void Restore()
    {
        if (!IsArchived)
        {
            throw new BusinessRuleException("catalog.not_archived", "Позиция не в архиве.");
        }

        IsArchived = false;
        ArchivedAtUtc = null;
    }

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private void Set(string code, string name, ItemType type, long unitId, string? description)
    {
        var c = NormalizeCode(code);
        if (c.Length is 0 or > CodeMaxLength || c.Any(char.IsWhiteSpace))
        {
            throw new BusinessRuleException("catalog.item.code_invalid", $"Код: 1–{CodeMaxLength} символов без пробелов.");
        }

        if (!ItemTypes.IsDefined(type))
        {
            throw new BusinessRuleException("catalog.item.type_invalid", "Выберите тип номенклатуры.");
        }

        Code = c;
        Name = DomainText.Require(name, NameMaxLength, "Наименование");
        Type = type;
        UnitId = unitId;
        Description = DomainText.Optional(description, DescriptionMaxLength, "Описание");
    }
}
