using System.Globalization;

namespace KnitErp.Web.Components.Shared;

/// <summary>Денежные суммы на экране: «61 000,00» — разряды пробелом, копейки всегда (формат счетов и платёжек).</summary>
public static class Amounts
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Format(decimal value) => value.ToString("#,0.00", Ru);

    public static string Format(decimal? value) => value is { } v ? Format(v) : "—";

    /// <summary>Процент НДС строки: «22%», «без НДС».</summary>
    public static string Vat(decimal? percent) => percent is { } p ? $"{p:0.##}%" : "без НДС";
}
