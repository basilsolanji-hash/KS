using KnitErp.Domain.Common;

namespace KnitErp.Domain.Purchasing;

public enum ReceivedVatInvoiceStatus : byte
{
    Registered = 2,
    Cancelled = 9,
}

/// <summary>
/// Полученный счёт-фактура (или УПД) поставщика, зарегистрированный к приёмке по заказу (D70). Номер и дата — как в документе
/// поставщика; суммы — из документа (по умолчанию подставляются из приёмки по ценам заказа). Ошибочная запись не удаляется,
/// а отменяется с причиной. К одной приёмке — один действующий счёт-фактура.
/// </summary>
public sealed class ReceivedVatInvoice
{
    public const int NumberMaxLength = 50;
    public const int CommentMaxLength = 500;

    private ReceivedVatInvoice()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string SupplierNumber { get; private set; } = string.Empty;
    public DateOnly InvoiceDate { get; private set; }
    public long SupplierId { get; private set; }
    public long ReceiptDocumentId { get; private set; }

    /// <summary>Сумма с НДС.</summary>
    public decimal Amount { get; private set; }

    public decimal VatAmount { get; private set; }
    public string? Comment { get; private set; }
    public ReceivedVatInvoiceStatus Status { get; private set; }
    public long CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public long? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public decimal AmountWithoutVat => Amount - VatAmount;

    public static ReceivedVatInvoice Register(
        long organizationId, long supplierId, long receiptDocumentId, string? number, DateOnly date, decimal amount, decimal vatAmount, string? comment,
        long userId, DateTime nowUtc)
    {
        Validate(number, amount, vatAmount, comment);
        return new ReceivedVatInvoice
        {
            OrganizationId = organizationId,
            SupplierId = supplierId,
            ReceiptDocumentId = receiptDocumentId,
            SupplierNumber = number!.Trim(),
            InvoiceDate = date,
            Amount = amount,
            VatAmount = vatAmount,
            Comment = DomainText.Optional(comment, CommentMaxLength, "Комментарий"),
            Status = ReceivedVatInvoiceStatus.Registered,
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc,
        };
    }

    /// <summary>Проверка до записи: номер обязателен, суммы до копеек, НДС не больше суммы.</summary>
    public static void Validate(string? number, decimal amount, decimal vatAmount, string? comment)
    {
        DomainText.Require(number, NumberMaxLength, "Номер счёта-фактуры");
        DomainText.Optional(comment, CommentMaxLength, "Комментарий");
        if (amount <= 0 || Money.Round(amount) != amount || vatAmount < 0 || Money.Round(vatAmount) != vatAmount || vatAmount >= amount)
        {
            throw new BusinessRuleException("purchase.vat_invoice.amount", "Сумма — больше нуля, НДС — не меньше нуля и меньше суммы, до копеек.");
        }
    }

    public void Cancel(long userId, string? reason, DateTime nowUtc)
    {
        if (Status != ReceivedVatInvoiceStatus.Registered)
        {
            throw new BusinessRuleException("purchase.vat_invoice.cancelled", "Счёт-фактура уже отменён.");
        }

        CancelReason = DomainText.Require(reason, CommentMaxLength, "Причина отмены");
        Status = ReceivedVatInvoiceStatus.Cancelled;
        CancelledByUserId = userId;
        CancelledAtUtc = nowUtc;
    }
}
