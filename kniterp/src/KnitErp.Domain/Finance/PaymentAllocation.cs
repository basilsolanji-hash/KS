using KnitErp.Domain.Common;

namespace KnitErp.Domain.Finance;

/// <summary>
/// Разноска платежа по заказу (D85): какая часть оплаты закрывает какой заказ. Не разнесённый остаток платежа — аванс
/// (переплата) контрагента. Разноска не удаляется: снятая остаётся в истории с отметкой, кто и когда её снял.
/// Долг контрагента от разноски не зависит — только то, какие заказы считаются оплаченными.
/// </summary>
public abstract class PaymentAllocation
{
    public long Id { get; protected set; }
    public long OrganizationId { get; protected set; }
    public long PaymentId { get; protected set; }
    public long OrderId { get; protected set; }
    public decimal Amount { get; protected set; }
    public long CreatedByUserId { get; protected set; }
    public DateTime CreatedAtUtc { get; protected set; }
    public long? RemovedByUserId { get; private set; }
    public DateTime? RemovedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive => RemovedAtUtc is null;

    public void Remove(long userId, DateTime nowUtc)
    {
        if (!IsActive)
        {
            throw new BusinessRuleException("payment.allocation.removed", "Разноска уже снята.");
        }

        RemovedByUserId = userId;
        RemovedAtUtc = nowUtc;
    }

    protected void Init(long organizationId, long paymentId, long orderId, decimal amount, long userId, DateTime nowUtc)
    {
        if (amount <= 0 || Money.Round(amount) != amount)
        {
            throw new BusinessRuleException("payment.allocation.amount", "Сумма разноски — больше нуля, до копеек.");
        }

        OrganizationId = organizationId;
        PaymentId = paymentId;
        OrderId = orderId;
        Amount = amount;
        CreatedByUserId = userId;
        CreatedAtUtc = nowUtc;
    }
}

/// <summary>Разноска оплаты покупателя по заказу покупателя.</summary>
public sealed class CustomerPaymentAllocation : PaymentAllocation
{
    private CustomerPaymentAllocation()
    {
    }

    public static CustomerPaymentAllocation Create(long organizationId, long paymentId, long orderId, decimal amount, long userId, DateTime nowUtc)
    {
        var a = new CustomerPaymentAllocation();
        a.Init(organizationId, paymentId, orderId, amount, userId, nowUtc);
        return a;
    }
}

/// <summary>Разноска оплаты поставщику по заказу поставщику.</summary>
public sealed class SupplierPaymentAllocation : PaymentAllocation
{
    private SupplierPaymentAllocation()
    {
    }

    public static SupplierPaymentAllocation Create(long organizationId, long paymentId, long orderId, decimal amount, long userId, DateTime nowUtc)
    {
        var a = new SupplierPaymentAllocation();
        a.Init(organizationId, paymentId, orderId, amount, userId, nowUtc);
        return a;
    }
}

/// <summary>
/// Распределение суммы по заказам «сверху вниз»: каждому — не больше его остатка к оплате, пока сумма не кончится.
/// Порядок задаёт вызывающий (сначала просроченные, затем по сроку и дате).
/// </summary>
public static class PaymentDistribution
{
    public static IReadOnlyList<(long OrderId, decimal Amount)> Distribute(decimal amount, IEnumerable<(long OrderId, decimal Outstanding)> orders)
    {
        var result = new List<(long, decimal)>();
        var left = Money.Round(amount);
        foreach (var (orderId, outstanding) in orders)
        {
            if (left <= 0)
            {
                break;
            }

            var part = Math.Min(left, Money.Round(outstanding));
            if (part > 0)
            {
                result.Add((orderId, part));
                left -= part;
            }
        }

        return result;
    }
}
