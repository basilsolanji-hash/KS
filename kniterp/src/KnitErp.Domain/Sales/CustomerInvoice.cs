using KnitErp.Domain.Common;

namespace KnitErp.Domain.Sales;

public enum CustomerInvoiceStatus : byte
{
    Issued = 2,
    Cancelled = 9,
}

/// <summary>
/// Счёт покупателю на оплату (D68): выставляется по подтверждённому заказу и копирует его строки, цены и НДС на дату счёта.
/// Счёт не меняется: другие условия — отменить с причиной и выставить новый. У заказа один действующий счёт.
/// Оплаты привязаны к заказу, поэтому оплаченность счёта считается по оплатам его заказа.
/// </summary>
public sealed class CustomerInvoice
{
    public const string NumberPrefix = "СЧ";
    public const int CommentMaxLength = 500;

    private readonly List<CustomerInvoiceLine> _lines = [];

    private CustomerInvoice()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateOnly InvoiceDate { get; private set; }

    /// <summary>Оплатить до. Пусто — срок не указан.</summary>
    public DateOnly? DueDate { get; private set; }

    public long SalesOrderId { get; private set; }
    public long CustomerId { get; private set; }
    public bool PricesIncludeVat { get; private set; }
    public CustomerInvoiceStatus Status { get; private set; }
    public string? Comment { get; private set; }
    public long CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public long? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<CustomerInvoiceLine> Lines => _lines;

    public decimal Total => _lines.Sum(l => l.Amount);
    public decimal VatTotal => _lines.Sum(l => l.VatAmount);

    public static CustomerInvoice Create(
        long organizationId, string number, DateOnly date, DateOnly? dueDate, SalesOrder order, string? comment, long userId, DateTime nowUtc)
    {
        EnsureCanIssue(order, date, dueDate);
        var invoice = new CustomerInvoice
        {
            OrganizationId = organizationId,
            Number = number,
            InvoiceDate = date,
            DueDate = dueDate,
            SalesOrderId = order.Id,
            CustomerId = order.CustomerId,
            PricesIncludeVat = order.PricesIncludeVat,
            Status = CustomerInvoiceStatus.Issued,
            Comment = DomainText.Optional(comment, CommentMaxLength, "Комментарий"),
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc,
        };
        foreach (var l in order.Lines)
        {
            invoice._lines.Add(new CustomerInvoiceLine(l.ItemId, l.Quantity, l.Price, l.VatPercent, l.Amount, l.VatAmount));
        }

        return invoice;
    }

    /// <summary>Проверка до выдачи номера: номер не тратится на счёт, который не выставится.</summary>
    public static void EnsureCanIssue(SalesOrder order, DateOnly date, DateOnly? dueDate, string? comment = null)
    {
        DomainText.Optional(comment, CommentMaxLength, "Комментарий");
        if (order.Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.Closed))
        {
            throw new BusinessRuleException("sales.invoice.order_status", "Счёт выставляется по подтверждённому заказу.");
        }

        if (dueDate is { } due && due < date)
        {
            throw new BusinessRuleException("sales.invoice.due_date", "Срок оплаты раньше даты счёта.");
        }
    }

    public void Cancel(long userId, string? reason, DateTime nowUtc)
    {
        if (Status != CustomerInvoiceStatus.Issued)
        {
            throw new BusinessRuleException("sales.invoice.cancelled", "Счёт уже отменён.");
        }

        CancelReason = DomainText.Require(reason, CommentMaxLength, "Причина отмены");
        Status = CustomerInvoiceStatus.Cancelled;
        CancelledByUserId = userId;
        CancelledAtUtc = nowUtc;
    }

    public static string StatusName(CustomerInvoiceStatus status) => status switch
    {
        CustomerInvoiceStatus.Issued => "Выставлен",
        CustomerInvoiceStatus.Cancelled => "Отменён",
        _ => status.ToString(),
    };
}

/// <summary>Строка счёта — копия строки заказа на момент выставления.</summary>
public sealed class CustomerInvoiceLine
{
    private CustomerInvoiceLine()
    {
    }

    internal CustomerInvoiceLine(long itemId, decimal quantity, decimal price, decimal? vatPercent, decimal amount, decimal vatAmount)
    {
        ItemId = itemId;
        Quantity = quantity;
        Price = price;
        VatPercent = vatPercent;
        Amount = amount;
        VatAmount = vatAmount;
    }

    public long Id { get; private set; }
    public long InvoiceId { get; private set; }
    public long ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Price { get; private set; }
    public decimal? VatPercent { get; private set; }

    /// <summary>Сумма строки с НДС.</summary>
    public decimal Amount { get; private set; }

    public decimal VatAmount { get; private set; }
}
