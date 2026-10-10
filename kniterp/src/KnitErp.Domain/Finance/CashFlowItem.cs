using KnitErp.Domain.Common;

namespace KnitErp.Domain.Finance;

/// <summary>Направление статьи движения денег.</summary>
public enum CashFlowDirection : byte
{
    In = 1,
    Out = 2,
}

/// <summary>
/// Статья движения денежных средств (ДДС, D86): откуда пришли и на что ушли деньги. Справочник организации: стандартные статьи
/// создаются вместе с организацией, свои можно добавлять, переименовывать и архивировать. Системные статьи (оплаты покупателей и
/// поставщикам, подотчёт, прочее) не переименовываются и не архивируются: на них опираются отчёты и правила.
/// Перемещения между своими счетами в ДДС не входят — это не поступление и не выплата.
/// </summary>
public sealed class CashFlowItem
{
    public const int NameMaxLength = 100;

    /// <summary>Системные коды.</summary>
    public const string CustomerPayments = "customer_payments";
    public const string SupplierPayments = "supplier_payments";
    public const string AccountableIssue = "accountable_issue";
    public const string AccountableReturn = "accountable_return";
    public const string OtherIncome = "other_income";
    public const string OtherExpense = "other_expense";

    /// <summary>Стандартные статьи новой организации: (направление, название, системный код).</summary>
    public static readonly IReadOnlyList<(CashFlowDirection Direction, string Name, string? Code)> Defaults =
    [
        (CashFlowDirection.In, "Поступления от покупателей", CustomerPayments),
        (CashFlowDirection.In, "Возврат подотчётных сумм", AccountableReturn),
        (CashFlowDirection.In, "Получение займов и кредитов", null),
        (CashFlowDirection.In, "Взносы учредителей", null),
        (CashFlowDirection.In, "Проценты банка", null),
        (CashFlowDirection.In, "Прочие поступления", OtherIncome),
        (CashFlowDirection.Out, "Оплата поставщикам", SupplierPayments),
        (CashFlowDirection.Out, "Выдача под отчёт", AccountableIssue),
        (CashFlowDirection.Out, "Заработная плата", null),
        (CashFlowDirection.Out, "Налоги и страховые взносы", null),
        (CashFlowDirection.Out, "Аренда", null),
        (CashFlowDirection.Out, "Банковские комиссии", null),
        (CashFlowDirection.Out, "Возврат займов и кредитов", null),
        (CashFlowDirection.Out, "Прочие выплаты", OtherExpense),
    ];

    private CashFlowItem()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public CashFlowDirection Direction { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? SystemCode { get; private set; }
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsSystem => SystemCode is not null;

    public static CashFlowItem Create(long organizationId, CashFlowDirection direction, string? name, string? systemCode = null) => new()
    {
        OrganizationId = organizationId,
        Direction = direction,
        Name = DomainText.Require(name, NameMaxLength, "Название статьи"),
        SystemCode = systemCode,
    };

    public void Rename(string? name)
    {
        EnsureEditable();
        Name = DomainText.Require(name, NameMaxLength, "Название статьи");
    }

    public void SetArchived(bool archived)
    {
        EnsureEditable();
        IsArchived = archived;
    }

    public static string DirectionName(CashFlowDirection direction) => direction == CashFlowDirection.In ? "Поступление" : "Выплата";

    private void EnsureEditable()
    {
        if (IsSystem)
        {
            throw new BusinessRuleException("cash_flow_item.system", $"Статья «{Name}» системная — её нельзя переименовать или убрать в архив.");
        }
    }
}
