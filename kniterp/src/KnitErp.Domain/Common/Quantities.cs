using System.Globalization;

namespace KnitErp.Domain.Common;

public static class Quantities
{
    /// <summary>Количество из текста: «12,5», «12.5», «1 250» — все варианты, которые даёт Excel в русской локали.</summary>
    public static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Replace(" ", "").Replace(" ", "").Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    public static string Format(decimal quantity) => quantity.ToString("0.######", CultureInfo.GetCultureInfo("ru-RU"));

    /// <summary>Количество строки документа: больше нуля и в пределах decimal(18,6).</summary>
    public static void EnsurePositive(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("stock.quantity.positive", "Количество должно быть больше нуля.");
        }

        if (quantity >= 1_000_000_000_000m)
        {
            throw new BusinessRuleException("stock.quantity.too_large", "Слишком большое количество.");
        }
    }
}
