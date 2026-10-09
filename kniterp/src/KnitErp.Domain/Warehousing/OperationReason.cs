using KnitErp.Domain.Common;

namespace KnitErp.Domain.Warehousing;

/// <summary>Вид складской операции, к которому относится причина. Числа хранятся в базе и закреплены CHECK.</summary>
public enum StockOperationKind : byte
{
    Receipt = 1,
    WriteOff = 2,
    Transfer = 3,
    Inventory = 4,
}

public static class StockOperationKinds
{
    public static readonly IReadOnlyList<StockOperationKind> All =
        [StockOperationKind.Receipt, StockOperationKind.WriteOff, StockOperationKind.Transfer, StockOperationKind.Inventory];

    public static string Name(StockOperationKind kind) => kind switch
    {
        StockOperationKind.Receipt => "Поступление",
        StockOperationKind.WriteOff => "Списание",
        StockOperationKind.Transfer => "Перемещение",
        StockOperationKind.Inventory => "Инвентаризация",
        _ => kind.ToString(),
    };
}

/// <summary>
/// Причина складской операции («Брак», «Передача в производство»). Документ склада без причины не проводится —
/// так отчёт по списаниям объясняет, куда ушёл материал. С флагом RequiresComment документ требует пояснения.
/// </summary>
public sealed class OperationReason
{
    public const int NameMaxLength = 150;

    private OperationReason()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public StockOperationKind Kind { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool RequiresComment { get; private set; }
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Стандартный набор для новой организации. Тот же список — в миграции AddCounterpartiesAndReasons.</summary>
    public static readonly IReadOnlyList<(StockOperationKind Kind, string Name, bool RequiresComment)> Defaults =
    [
        (StockOperationKind.Receipt, "Закупка у поставщика", false),
        (StockOperationKind.Receipt, "Выпуск из производства", false),
        (StockOperationKind.Receipt, "Возврат из производства", false),
        (StockOperationKind.WriteOff, "Передача в производство", false),
        (StockOperationKind.WriteOff, "Брак", true),
        (StockOperationKind.WriteOff, "Порча", true),
        (StockOperationKind.Transfer, "Перемещение между складами", false),
        (StockOperationKind.Inventory, "Излишек по инвентаризации", false),
        (StockOperationKind.Inventory, "Недостача по инвентаризации", true),
    ];

    public static OperationReason Create(long organizationId, StockOperationKind kind, string name, bool requiresComment)
    {
        if (!StockOperationKinds.All.Contains(kind))
        {
            throw new BusinessRuleException("warehouse.reason.kind", "Выберите вид операции.");
        }

        return new OperationReason
        {
            OrganizationId = organizationId,
            Kind = kind,
            Name = DomainText.Require(name, NameMaxLength, "Причина"),
            RequiresComment = requiresComment,
        };
    }

    public Organizations.FieldChange? Update(string name, bool requiresComment)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Причина в архиве.");
        }

        var before = $"{Name}{(RequiresComment ? " (нужно пояснение)" : "")}";
        Name = DomainText.Require(name, NameMaxLength, "Причина");
        RequiresComment = requiresComment;
        var after = $"{Name}{(RequiresComment ? " (нужно пояснение)" : "")}";
        return before == after ? null : new Organizations.FieldChange("Причина", before, after);
    }

    public void Archive()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Причина уже в архиве.");
        }

        IsArchived = true;
    }
}
