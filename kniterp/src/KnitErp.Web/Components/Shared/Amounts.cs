using System.Globalization;

namespace KnitErp.Web.Components.Shared;

/// <summary>Денежные суммы на экране: «61 000,00» — разряды пробелом, копейки всегда (формат счетов и платёжек).</summary>
public static class Amounts
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Format(decimal value) => value.ToString("#,0.00", Ru);

    public static string Format(decimal? value) => value is { } v ? Format(v) : "—";

    /// <summary>Коротко для осей и плиток: «950», «61,2 тыс.», «1,4 млн».</summary>
    public static string Compact(decimal value)
    {
        var abs = Math.Abs(value);
        return abs >= 1_000_000m ? (value / 1_000_000m).ToString("0.#", Ru) + " " + KnitErp.Web.Localization.Text.L("млн")
            : abs >= 1_000m ? (value / 1_000m).ToString("0.#", Ru) + " " + KnitErp.Web.Localization.Text.L("тыс.")
            : value.ToString("0", Ru);
    }

    /// <summary>Процент НДС строки: «22%», «без НДС».</summary>
    public static string Vat(decimal? percent) => percent is { } p ? $"{p:0.##}%" : "без НДС";

    /// <summary>
    /// Цена из прайса для строки документа (D79): прайс с НДС, а документ без — НДС вычитается, и наоборот; до 4 знаков.
    /// </summary>
    public static decimal Reprice(decimal price, bool priceIncludesVat, bool documentIncludesVat, decimal? vatPercent)
    {
        var rate = (vatPercent ?? 0) / 100m;
        if (priceIncludesVat == documentIncludesVat || rate == 0)
        {
            return price;
        }

        return decimal.Round(priceIncludesVat ? price / (1 + rate) : price * (1 + rate), 4);
    }

    /// <summary>Цена для поля ввода: «1 234,5» без лишних нулей.</summary>
    public static string Input(decimal value) => value.ToString("0.####", Ru);
}
