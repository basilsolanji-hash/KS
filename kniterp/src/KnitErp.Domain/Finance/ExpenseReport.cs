using KnitErp.Domain.Common;

namespace KnitErp.Domain.Finance;

public enum ExpenseReportStatus : byte
{
    Draft = 1,
    Approved = 2,
    Cancelled = 9,
}

/// <summary>
/// Авансовый отчёт подотчётного лица (D86, форма АО-1 по постановлению Госкомстата России № 55): на что потрачены деньги,
/// выданные под отчёт. Черновик правится; утверждённый уменьшает долг сотрудника. Ошибка в утверждённом — отмена с причиной
/// и новый отчёт. Срок отчёта — не позже 3 рабочих дней после срока выдачи или выхода на работу (п. 6.3 Указания № 3210-У).
/// </summary>
public sealed class ExpenseReport
{
    public const string NumberPrefix = "АО";
    public const int PurposeMaxLength = 300;
    public const int ReasonMaxLength = 500;

    private readonly List<ExpenseReportLine> _lines = [];

    private ExpenseReport()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateOnly ReportDate { get; private set; }
    public long EmployeeId { get; private set; }
    public long LegalEntityId { get; private set; }

    /// <summary>Назначение аванса: «Хозяйственные нужды», «Командировка в г. Иваново».</summary>
    public string? Purpose { get; private set; }

    public ExpenseReportStatus Status { get; private set; }
    public long CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public long? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public long? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }

    /// <summary>Растёт при каждой правке строк: защищает от потери правки при одновременной работе.</summary>
    public int LinesRevision { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<ExpenseReportLine> Lines => _lines;

    public decimal Total => _lines.Sum(l => l.Amount);

    public static ExpenseReport Create(long organizationId, string number, DateOnly date, long employeeId, long legalEntityId, string? purpose, long userId,
        DateTime nowUtc) => new()
    {
        OrganizationId = organizationId,
        Number = number,
        ReportDate = date,
        EmployeeId = employeeId,
        LegalEntityId = legalEntityId,
        Purpose = DomainText.Optional(purpose, PurposeMaxLength, "Назначение аванса"),
        Status = ExpenseReportStatus.Draft,
        CreatedByUserId = userId,
        CreatedAtUtc = nowUtc,
    };

    public void UpdateHeader(DateOnly date, string? purpose)
    {
        EnsureDraft();
        ReportDate = date;
        Purpose = DomainText.Optional(purpose, PurposeMaxLength, "Назначение аванса");
    }

    public ExpenseReportLine AddLine(DateOnly documentDate, string? document, string? description, decimal amount, long cashFlowItemId)
    {
        EnsureDraft();
        if (_lines.Count >= 200)
        {
            throw new BusinessRuleException("expense_report.lines", "В одном отчёте — не больше 200 строк.");
        }

        var line = ExpenseReportLine.Create(documentDate, document, description, amount, cashFlowItemId);
        _lines.Add(line);
        LinesRevision++;
        return line;
    }

    public void RemoveLine(long lineId)
    {
        EnsureDraft();
        var line = _lines.SingleOrDefault(l => l.Id == lineId)
                   ?? throw new BusinessRuleException("expense_report.line", "Строка не найдена — возможно, её уже удалили.");
        _lines.Remove(line);
        LinesRevision++;
    }

    public void Approve(long userId, DateTime nowUtc)
    {
        EnsureDraft();
        if (_lines.Count == 0)
        {
            throw new BusinessRuleException("expense_report.empty", "В отчёте нет расходов — добавьте документы.");
        }

        if (_lines.Any(l => l.DocumentDate > ReportDate))
        {
            throw new BusinessRuleException("expense_report.document_date", "Дата документа расхода позже даты отчёта.");
        }

        Status = ExpenseReportStatus.Approved;
        ApprovedByUserId = userId;
        ApprovedAtUtc = nowUtc;
    }

    public void Cancel(long userId, string? reason, DateTime nowUtc)
    {
        if (Status == ExpenseReportStatus.Cancelled)
        {
            throw new BusinessRuleException("expense_report.cancelled", "Отчёт уже отменён.");
        }

        CancelReason = DomainText.Require(reason, ReasonMaxLength, "Причина отмены");
        Status = ExpenseReportStatus.Cancelled;
        CancelledByUserId = userId;
        CancelledAtUtc = nowUtc;
    }

    public static string StatusName(ExpenseReportStatus status) => status switch
    {
        ExpenseReportStatus.Draft => "Черновик",
        ExpenseReportStatus.Approved => "Утверждён",
        _ => "Отменён",
    };

    private void EnsureDraft()
    {
        if (Status != ExpenseReportStatus.Draft)
        {
            throw new BusinessRuleException("expense_report.not_draft", "Отчёт уже утверждён или отменён — менять его нельзя.");
        }
    }
}

/// <summary>Строка авансового отчёта: оправдательный документ (чек, накладная, билет) и статья расхода.</summary>
public sealed class ExpenseReportLine
{
    public const int DocumentMaxLength = 100;
    public const int DescriptionMaxLength = 300;

    private ExpenseReportLine()
    {
    }

    public long Id { get; private set; }
    public long ExpenseReportId { get; private set; }
    public DateOnly DocumentDate { get; private set; }

    /// <summary>Документ: «Кассовый чек № 125», «Товарная накладная № 7».</summary>
    public string Document { get; private set; } = string.Empty;

    public string? Description { get; private set; }
    public decimal Amount { get; private set; }
    public long CashFlowItemId { get; private set; }

    internal static ExpenseReportLine Create(DateOnly documentDate, string? document, string? description, decimal amount, long cashFlowItemId)
    {
        if (amount <= 0 || Money.Round(amount) != amount)
        {
            throw new BusinessRuleException("expense_report.amount", "Сумма расхода — больше нуля, до копеек.");
        }

        return new ExpenseReportLine
        {
            DocumentDate = documentDate,
            Document = DomainText.Require(document, DocumentMaxLength, "Документ"),
            Description = DomainText.Optional(description, DescriptionMaxLength, "Описание"),
            Amount = amount,
            CashFlowItemId = cashFlowItemId,
        };
    }
}
