using System.Globalization;

namespace KnitErp.Domain.Common;

public static class Quantities
{
    /// <summary>Количество из текста: «12,5», «12.5», «1 250» — все варианты, которые даёт Excel в русской локали.</summary>
    public static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Replace(" ", "").Replace(" ", "").Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    public static string Format(decimal quantity) => quantity.ToString("0.######", CultureInfo.GetCultureInfo("ru-RU"));
}
