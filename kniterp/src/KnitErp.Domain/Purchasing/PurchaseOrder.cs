using KnitErp.Domain.Common;

namespace KnitErp.Domain.Purchasing;

public enum PurchaseOrderStatus : byte
{
    Draft = 1,
    Confirmed = 2,
    Closed = 3,
    Cancelled = 9,
}

/// <summary>
/// Заказ поставщику (D64): что, сколько и почём покупаем. Черновик правится; «Подтвердить» фиксирует условия —
/// по подтверждённому заказу оформляются поступления и возвраты, их суммы считаются по ценам заказа, и из них
/// складывается долг поставщику. Цены — в валюте организации; НДС — по проценту строки.
/// </summary>
public sealed class PurchaseOrder
{
    public const string NumberPrefix = "ЗП";
    public const int CommentMaxLength = 1000;
    public const int InvoiceMaxLength = 100;
    public const int MaxLines = 500;

    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateOnly OrderDate { get; private set; }
    public long SupplierId { get; private set; }

    /// <summary>Склад, куда поступит товар.</summary>
    public long WarehouseId { get; private set; }

    public DateOnly? ExpectedDate { get; private set; }

    /// <summary>Счёт поставщика: номер и дата, как в его документе («№ 15 от 05.10.2026»).</summary>
    public string? SupplierInvoice { get; private set; }

    /// <summary>Цены строк с НДС (НДС выделяется из суммы) или без (НДС начисляется сверху).</summary>
    public bool PricesIncludeVat { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }
    public string? Comment { get; private set; }
    public long CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public long? ConfirmedByUserId { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    public decimal Total => _lines.Sum(l => l.Amount);
    public decimal VatTotal => _lines.Sum(l => l.VatAmount);

    public static PurchaseOrder Create(long organizationId, string number, PurchaseOrderHeader header, long userId, DateTime nowUtc)
    {
        var order = new PurchaseOrder
        {
            OrganizationId = organizationId,
            Number = number,
            Status = PurchaseOrderStatus.Draft,
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc,
        };
        order.ApplyHeader(header);
        return order;
    }

    public void UpdateHeader(PurchaseOrderHeader header)
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
            throw new BusinessRuleException("purchase.price", "Цена — не меньше нуля, не больше четырёх знаков после запятой.");
        }

        if (vatPercent is { } p && (p < 0 || p > 100 || decimal.Round(p, 2) != p))
        {
            throw new BusinessRuleException("purchase.vat", "Ставка НДС — от 0 до 100%.");
        }

        var line = _lines.FirstOrDefault(l => l.ItemId == itemId);
        if (line is null)
        {
            if (_lines.Count >= MaxLines)
            {
                throw new BusinessRuleException("purchase.too_many_lines", $"В заказе не больше {MaxLines} строк.");
            }

            line = new PurchaseOrderLine(itemId);
            _lines.Add(line);
        }

        line.Set(quantity, price, vatPercent, PricesIncludeVat);
    }

    public void RemoveLine(long itemId)
    {
        EnsureDraft();
        var line = _lines.FirstOrDefault(l => l.ItemId == itemId)
                   ?? throw new BusinessRuleException("purchase.line_missing", "Такой строки в заказе нет.");
        _lines.Remove(line);
    }

    public void Confirm(long userId, DateTime nowUtc)
    {
        EnsureDraft();
        if (_lines.Count == 0)
        {
            throw new BusinessRuleException("purchase.empty", "В заказе нет строк.");
        }

        Status = PurchaseOrderStatus.Confirmed;
        ConfirmedByUserId = userId;
        ConfirmedAtUtc = nowUtc;
    }

    /// <summary>Закрыть: больше поступлений по заказу не ждём (получен полностью или остаток не нужен).</summary>
    public void Close()
    {
        if (Status != PurchaseOrderStatus.Confirmed)
        {
            throw new BusinessRuleException("purchase.status", "Закрыть можно только подтверждённый заказ.");
        }

        Status = PurchaseOrderStatus.Closed;
    }

    /// <summary>Вернуть закрытый заказ в работу — например, поставщик довёз остаток.</summary>
    public void Reopen()
    {
        if (Status != PurchaseOrderStatus.Closed)
        {
            throw new BusinessRuleException("purchase.status", "Вернуть в работу можно только закрытый заказ.");
        }

        Status = PurchaseOrderStatus.Confirmed;
    }

    /// <summary>Отмена. По подтверждённому заказу — только если по нему нет проведённых документов и оплат (проверяет сервис).</summary>
    public void Cancel()
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Confirmed))
        {
            throw new BusinessRuleException("purchase.status", "Отменить можно черновик или подтверждённый заказ.");
        }

        Status = PurchaseOrderStatus.Cancelled;
    }

    /// <summary>Цена единицы с НДС — по ней оценивается поступление и возврат по заказу.</summary>
    public decimal UnitCostWithVat(long itemId) =>
        _lines.FirstOrDefault(l => l.ItemId == itemId) is { } l && l.Quantity != 0 ? l.Amount / l.Quantity : 0m;

    public static string StatusName(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "Черновик",
        PurchaseOrderStatus.Confirmed => "Подтверждён",
        PurchaseOrderStatus.Closed => "Закрыт",
        PurchaseOrderStatus.Cancelled => "Отменён",
        _ => status.ToString(),
    };

    private void ApplyHeader(PurchaseOrderHeader h)
    {
        if (h.ExpectedDate is { } expected && expected < h.OrderDate)
        {
            throw new BusinessRuleException("purchase.expected_date", "Ожидаемая дата поступления раньше даты заказа.");
        }

        OrderDate = h.OrderDate;
        SupplierId = h.SupplierId;
        WarehouseId = h.WarehouseId;
        ExpectedDate = h.ExpectedDate;
        SupplierInvoice = DomainText.Optional(h.SupplierInvoice, InvoiceMaxLength, "Счёт поставщика");
        PricesIncludeVat = h.PricesIncludeVat;
        Comment = DomainText.Optional(h.Comment, CommentMaxLength, "Комментарий");
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new BusinessRuleException("purchase.not_draft", "Заказ уже подтверждён — менять его нельзя. Нужны другие условия — отмените и оформите новый.");
        }
    }
}

public sealed record PurchaseOrderHeader(
    DateOnly OrderDate, long SupplierId, long WarehouseId, DateOnly? ExpectedDate, string? SupplierInvoice, bool PricesIncludeVat, string? Comment);

/// <summary>Строка заказа: количество, цена, процент НДС; сумма и НДС считаются и хранятся с округлением до копеек.</summary>
public sealed class PurchaseOrderLine
{
    private PurchaseOrderLine()
    {
    }

    internal PurchaseOrderLine(long itemId) => ItemId = itemId;

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

/// <summary>Денежные расчёты строк: до копеек, банковское округление не используется — как в счетах (от середины вверх).</summary>
public static class Money
{
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Сумма строки с НДС и сам НДС.</summary>
    public static (decimal Amount, decimal Vat) LineAmounts(decimal quantity, decimal price, decimal? vatPercent, bool pricesIncludeVat)
    {
        var sum = Round(quantity * price);
        if (vatPercent is not { } p || p == 0)
        {
            return (sum, 0m);
        }

        if (pricesIncludeVat)
        {
            return (sum, Round(sum * p / (100 + p)));
        }

        var vat = Round(sum * p / 100);
        return (sum + vat, vat);
    }
}

public enum SupplierPaymentStatus : byte
{
    Posted = 2,
    Cancelled = 9,
}

/// <summary>
/// Оплата поставщику (D64): дата, поставщик, сумма, при желании — заказ. Уменьшает долг поставщику сразу.
/// Ошибочная оплата не удаляется, а отменяется с причиной — в истории остаются обе записи.
/// </summary>
public sealed class SupplierPayment
{
    public const string NumberPrefix = "ОП";
    public const int CommentMaxLength = 500;

    private SupplierPayment()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateOnly PaymentDate { get; private set; }
    public long SupplierId { get; private set; }
    public long? PurchaseOrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Comment { get; private set; }
    public SupplierPaymentStatus Status { get; private set; }
    public long CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public long? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SupplierPayment Create(
        long organizationId, string number, DateOnly date, long supplierId, long? orderId, decimal amount, string? comment, long userId, DateTime nowUtc)
    {
        if (amount <= 0 || Money.Round(amount) != amount)
        {
            throw new BusinessRuleException("purchase.payment.amount", "Сумма оплаты — больше нуля, до копеек.");
        }

        return new SupplierPayment
        {
            OrganizationId = organizationId,
            Number = number,
            PaymentDate = date,
            SupplierId = supplierId,
            PurchaseOrderId = orderId,
            Amount = amount,
            Comment = DomainText.Optional(comment, CommentMaxLength, "Комментарий"),
            Status = SupplierPaymentStatus.Posted,
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc,
        };
    }

    public void Cancel(long userId, string? reason, DateTime nowUtc)
    {
        if (Status != SupplierPaymentStatus.Posted)
        {
            throw new BusinessRuleException("purchase.payment.cancelled", "Оплата уже отменена.");
        }

        CancelReason = DomainText.Require(reason, CommentMaxLength, "Причина отмены");
        Status = SupplierPaymentStatus.Cancelled;
        CancelledByUserId = userId;
        CancelledAtUtc = nowUtc;
    }
}
