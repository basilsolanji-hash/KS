using KnitErp.Domain.Common;

namespace KnitErp.Domain.Sales;

public enum SalesOrderStatus : byte
{
    Draft = 1,
    Confirmed = 2,
    Closed = 3,
    Cancelled = 9,
}

/// <summary>
/// Заказ покупателя (D65): что, сколько и почём продаём. Черновик правится; «Подтвердить» фиксирует условия —
/// по подтверждённому заказу оформляются отгрузки и возвраты, их суммы считаются по ценам заказа, и из них
/// складывается долг покупателя. Цены — в валюте организации; НДС — по проценту строки.
/// </summary>
public sealed class SalesOrder
{
    public const string NumberPrefix = "ЗК";
    public const int CommentMaxLength = 1000;
    public const int InvoiceMaxLength = 100;
    public const int MaxLines = 500;

    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateOnly OrderDate { get; private set; }
    public long CustomerId { get; private set; }

    /// <summary>Склад, с которого отгружается товар.</summary>
    public long WarehouseId { get; private set; }

    public DateOnly? ShipDate { get; private set; }

    /// <summary>Договор или заказ покупателя: номер и дата, как в его документе («Договор № 7 от 01.10.2026»).</summary>
    public string? CustomerReference { get; private set; }

    /// <summary>Цены строк с НДС (НДС выделяется из суммы) или без (НДС начисляется сверху).</summary>
    public bool PricesIncludeVat { get; private set; }

    public SalesOrderStatus Status { get; private set; }

    /// <summary>Этап работы с заказом из справочника организации (D75); null — этап не выбран.</summary>
    public long? StageId { get; private set; }

    public string? Comment { get; private set; }
    public long CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public long? ConfirmedByUserId { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public decimal Total => _lines.Sum(l => l.Amount);
    public decimal VatTotal => _lines.Sum(l => l.VatAmount);

    public static SalesOrder Create(long organizationId, string number, SalesOrderHeader header, long userId, DateTime nowUtc)
    {
        var order = new SalesOrder
        {
            OrganizationId = organizationId,
            Number = number,
            Status = SalesOrderStatus.Draft,
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc,
        };
        order.ApplyHeader(header);
        return order;
    }

    public void UpdateHeader(SalesOrderHeader header)
    {
        EnsureDraft();
        var vatModeChanged = header.PricesIncludeVat != PricesIncludeVat;
        ApplyHeader(header);
        if (vatModeChanged)
        {
            foreach (var line in _lines)
            {
                line.Recalculate(PricesIncludeVat);
            }
        }
    }

    /// <summary>Строка по позиции: новая или замена. vatPercent null — «без НДС».</summary>
    public void SetLine(long itemId, decimal quantity, decimal price, decimal? vatPercent)
    {
        EnsureDraft();
        Quantities.EnsurePositive(quantity);
        if (price < 0 || decimal.Round(price, 4) != price)
        {
            throw new BusinessRuleException("sales.price", "Цена — не меньше нуля, не больше четырёх знаков после запятой.");
        }

        if (vatPercent is { } p && (p < 0 || p > 100 || decimal.Round(p, 2) != p))
        {
            throw new BusinessRuleException("sales.vat", "Ставка НДС — от 0 до 100%.");
        }

        var line = _lines.FirstOrDefault(l => l.ItemId == itemId);
        if (line is null)
        {
            if (_lines.Count >= MaxLines)
            {
                throw new BusinessRuleException("sales.too_many_lines", $"В заказе не больше {MaxLines} строк.");
            }

            line = new SalesOrderLine(itemId);
            _lines.Add(line);
        }

        line.Set(quantity, price, vatPercent, PricesIncludeVat);
    }

    public void RemoveLine(long itemId)
    {
        EnsureDraft();
        var line = _lines.FirstOrDefault(l => l.ItemId == itemId)
                   ?? throw new BusinessRuleException("sales.line_missing", "Такой строки в заказе нет.");
        _lines.Remove(line);
    }

    /// <summary>Этап меняется в любом состоянии, кроме отменённого: это метка работы, а не учёт.</summary>
    public void SetStage(long? stageId)
    {
        if (Status == SalesOrderStatus.Cancelled)
        {
            throw new BusinessRuleException("sales.order.cancelled", "Заказ отменён — этап не меняется.");
        }

        StageId = stageId;
    }

    public void Confirm(long userId, DateTime nowUtc)
    {
        EnsureDraft();
        if (_lines.Count == 0)
        {
            throw new BusinessRuleException("sales.empty", "В заказе нет строк.");
        }

        Status = SalesOrderStatus.Confirmed;
        ConfirmedByUserId = userId;
        ConfirmedAtUtc = nowUtc;
    }

    /// <summary>Закрыть: больше отгрузок по заказу не будет (отгружен полностью или остаток не нужен).</summary>
    public void Close()
    {
        if (Status != SalesOrderStatus.Confirmed)
        {
            throw new BusinessRuleException("sales.status", "Закрыть можно только подтверждённый заказ.");
        }

        Status = SalesOrderStatus.Closed;
    }

    /// <summary>Вернуть закрытый заказ в работу — например, покупатель всё-таки забирает остаток.</summary>
    public void Reopen()
    {
        if (Status != SalesOrderStatus.Closed)
        {
            throw new BusinessRuleException("sales.status", "Вернуть в работу можно только закрытый заказ.");
        }

        Status = SalesOrderStatus.Confirmed;
    }

    /// <summary>Отмена. По подтверждённому заказу — только если по нему нет проведённых документов и оплат (проверяет сервис).</summary>
    public void Cancel()
    {
        if (Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Confirmed))
        {
            throw new BusinessRuleException("sales.status", "Отменить можно черновик или подтверждённый заказ.");
        }

        Status = SalesOrderStatus.Cancelled;
    }

    /// <summary>Цена единицы с НДС — по ней оценивается отгрузка и возврат по заказу.</summary>
    public decimal UnitCostWithVat(long itemId) =>
        _lines.FirstOrDefault(l => l.ItemId == itemId) is { } l && l.Quantity != 0 ? l.Amount / l.Quantity : 0m;

    public static string StatusName(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.Draft => "Черновик",
        SalesOrderStatus.Confirmed => "Подтверждён",
        SalesOrderStatus.Closed => "Закрыт",
        SalesOrderStatus.Cancelled => "Отменён",
        _ => status.ToString(),
    };

    private void ApplyHeader(SalesOrderHeader h)
    {
        if (h.ShipDate is { } expected && expected < h.OrderDate)
        {
            throw new BusinessRuleException("sales.expected_date", "Дата отгрузки раньше даты заказа.");
        }

        OrderDate = h.OrderDate;
        CustomerId = h.CustomerId;
        WarehouseId = h.WarehouseId;
        ShipDate = h.ShipDate;
        CustomerReference = DomainText.Optional(h.CustomerReference, InvoiceMaxLength, "Договор покупателя");
        PricesIncludeVat = h.PricesIncludeVat;
        Comment = DomainText.Optional(h.Comment, CommentMaxLength, "Комментарий");
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw new BusinessRuleException("sales.not_draft", "Заказ уже подтверждён — менять его нельзя. Нужны другие условия — отмените и оформите новый.");
        }
    }
}

public sealed record SalesOrderHeader(
    DateOnly OrderDate, long CustomerId, long WarehouseId, DateOnly? ShipDate, string? CustomerReference, bool PricesIncludeVat, string? Comment);

/// <summary>Строка заказа: количество, цена, процент НДС; сумма и НДС считаются и хранятся с округлением до копеек.</summary>
public sealed class SalesOrderLine
{
    private SalesOrderLine()
    {
    }

    internal SalesOrderLine(long itemId) => ItemId = itemId;

    public long Id { get; private set; }
    public long OrderId { get; private set; }
    public long ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Price { get; private set; }

    /// <summary>Процент НДС; null — без НДС.</summary>
    public decimal? VatPercent { get; private set; }

    /// <summary>Сумма строки с НДС.</summary>
    public decimal Amount { get; private set; }

    public decimal VatAmount { get; private set; }

    internal void Set(decimal quantity, decimal price, decimal? vatPercent, bool pricesIncludeVat)
    {
        Quantity = quantity;
        Price = price;
        VatPercent = vatPercent;
        Recalculate(pricesIncludeVat);
    }

    internal void Recalculate(bool pricesIncludeVat) =>
        (Amount, VatAmount) = Money.LineAmounts(Quantity, Price, VatPercent, pricesIncludeVat);
}

public enum CustomerPaymentStatus : byte
{
    Posted = 2,
    Cancelled = 9,
}

/// <summary>
/// Оплата от покупателя (D65): дата, покупатель, сумма, при желании — заказ. Уменьшает долг покупателя сразу.
/// Ошибочная оплата не удаляется, а отменяется с причиной — в истории остаются обе записи.
/// </summary>
public sealed class CustomerPayment
{
    public const string NumberPrefix = "ПО";
    public const int CommentMaxLength = 500;
    public const int DocumentNumberMaxLength = 30;

    private CustomerPayment()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateOnly PaymentDate { get; private set; }
    public long CustomerId { get; private set; }
    public long? SalesOrderId { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>
    /// Номер платёжного документа покупателя (платёжного поручения); дата — дата оплаты. Печатается в строке 5 счёта-фактуры
    /// при отгрузке в счёт предоплаты (подп. «з» п. 1 Правил заполнения, ПП № 1137).
    /// </summary>
    public string? DocumentNumber { get; private set; }

    public string? Comment { get; private set; }
    public CustomerPaymentStatus Status { get; private set; }
    public long CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public long? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static CustomerPayment Create(
        long organizationId, string number, DateOnly date, long customerId, long? orderId, decimal amount, string? comment, long userId, DateTime nowUtc,
        string? documentNumber = null)
    {
        if (amount <= 0 || Money.Round(amount) != amount)
        {
            throw new BusinessRuleException("sales.payment.amount", "Сумма оплаты — больше нуля, до копеек.");
        }

        return new CustomerPayment
        {
            DocumentNumber = DomainText.Optional(documentNumber, DocumentNumberMaxLength, "№ платёжного документа"),
            OrganizationId = organizationId,
            Number = number,
            PaymentDate = date,
            CustomerId = customerId,
            SalesOrderId = orderId,
            Amount = amount,
            Comment = DomainText.Optional(comment, CommentMaxLength, "Комментарий"),
            Status = CustomerPaymentStatus.Posted,
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc,
        };
    }

    public void Cancel(long userId, string? reason, DateTime nowUtc)
    {
        if (Status != CustomerPaymentStatus.Posted)
        {
            throw new BusinessRuleException("sales.payment.cancelled", "Оплата уже отменена.");
        }

        CancelReason = DomainText.Require(reason, CommentMaxLength, "Причина отмены");
        Status = CustomerPaymentStatus.Cancelled;
        CancelledByUserId = userId;
        CancelledAtUtc = nowUtc;
    }
}
