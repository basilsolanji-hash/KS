using KnitErp.Domain.Common;

namespace KnitErp.Domain.Sales;

/// <summary>
/// Этап (статус) заказа покупателя, который настраивает организация (D75): «В обработке», «Ждём оплату», «Собран»…
/// Это метка работы с заказом и её цвет в списке; учётное состояние (черновик, подтверждён, закрыт, отменён) — отдельно
/// и правилами не заменяется. Не удаляется — уходит в архив; у действующих этапов название уникально.
/// </summary>
public sealed class SalesOrderStage
{
    public const int NameMaxLength = 60;
    public const int MaxStages = 30;

    /// <summary>Цвета этапов — из набора интерфейса, чтобы метки читались в тёмной и светлой теме.</summary>
    public static readonly IReadOnlyList<string> Colors = ["gray", "blue", "teal", "green", "yellow", "orange", "red", "magenta", "violet"];

    /// <summary>Этапы новой организации — как привыкли в МойСклад, их можно переименовать и перекрасить.</summary>
    public static readonly IReadOnlyList<(string Name, string Color)> Defaults =
    [
        ("В обработке", "gray"),
        ("Ждём оплату", "blue"),
        ("Оплачен — в производство", "green"),
        ("В работе", "violet"),
        ("Собран", "teal"),
        ("Отгружен", "orange"),
    ];

    private SalesOrderStage()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Color { get; private set; } = "gray";
    public int SortOrder { get; private set; }
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SalesOrderStage Create(long organizationId, string? name, string? color, int sortOrder)
    {
        var stage = new SalesOrderStage { OrganizationId = organizationId, SortOrder = sortOrder };
        stage.Set(name, color);
        return stage;
    }

    public void Set(string? name, string? color)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Этап в архиве. Сначала верните его из архива.");
        }

        Name = DomainText.Require(name, NameMaxLength, "Название этапа");
        Color = color is not null && Colors.Contains(color) ? color
            : throw new BusinessRuleException("sales.stage.color", "Выберите цвет этапа из предложенных.");
    }

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public void Archive()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Этап уже в архиве.");
        }

        IsArchived = true;
    }

    public void Restore()
    {
        if (!IsArchived)
        {
            throw new BusinessRuleException("catalog.not_archived", "Этап не в архиве.");
        }

        IsArchived = false;
    }
}
