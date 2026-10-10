using KnitErp.Application.Finance;
using KnitErp.Domain.Common;
using KnitErp.Domain.Finance;

namespace KnitErp.UnitTests;

/// <summary>D85: распределение оплаты по заказам и строка НДС кассового ордера.</summary>
public sealed class PaymentAllocationTests
{
    [Fact]
    public void Distribution_fills_orders_top_down_and_never_exceeds_outstanding()
    {
        Assert.Equal([(1L, 1_000m), (2L, 200m)], PaymentDistribution.Distribute(1_200m, [(1, 1_000m), (2, 500m), (3, 50m)]));
        Assert.Equal([(1L, 100m), (3L, 50m)], PaymentDistribution.Distribute(150m, [(1, 100m), (2, 0m), (3, 80m)]));
        Assert.Equal([(2L, 30m)], PaymentDistribution.Distribute(30m, [(1, -5m), (2, 40m)]));
        Assert.Empty(PaymentDistribution.Distribute(0m, [(1, 10m)]));
        Assert.Equal(300m, PaymentDistribution.Distribute(1_000m, [(1, 100m), (2, 200m)]).Sum(x => x.Amount));
    }

    [Fact]
    public void Allocation_amount_is_positive_with_kopecks_and_removal_is_once()
    {
        Assert.Equal("payment.allocation.amount",
            Assert.Throws<BusinessRuleException>(() => CustomerPaymentAllocation.Create(1, 1, 1, 0m, 1, DateTime.UtcNow)).Code);
        Assert.Equal("payment.allocation.amount",
            Assert.Throws<BusinessRuleException>(() => SupplierPaymentAllocation.Create(1, 1, 1, 1.005m, 1, DateTime.UtcNow)).Code);
        var a = CustomerPaymentAllocation.Create(1, 1, 1, 10.5m, 1, DateTime.UtcNow);
        Assert.True(a.IsActive);
        a.Remove(2, DateTime.UtcNow);
        Assert.False(a.IsActive);
        Assert.Equal("payment.allocation.removed", Assert.Throws<BusinessRuleException>(() => a.Remove(2, DateTime.UtcNow)).Code);
    }

    [Fact]
    public void Cash_order_vat_line_follows_order_rates()
    {
        static string Norm(string s) => s.Replace(' ', ' ').Replace(' ', ' ');
        Assert.Equal("НДС 22% — 220,00 руб.", Norm(MoneyOperationService.VatText(1_220m, [22m, 22m])));
        Assert.Equal("НДС 10% — 10,00 руб.", Norm(MoneyOperationService.VatText(110m, [10m])));
        Assert.Equal(MoneyOperationService.NoVat, MoneyOperationService.VatText(100m, [null, 0m]));
        Assert.Equal(string.Empty, MoneyOperationService.VatText(100m, [22m, 10m]));
        Assert.Equal(string.Empty, MoneyOperationService.VatText(100m, []));
    }
}
