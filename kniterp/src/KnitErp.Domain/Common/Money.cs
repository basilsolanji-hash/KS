namespace KnitErp.Domain.Common;

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
