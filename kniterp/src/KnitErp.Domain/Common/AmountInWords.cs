namespace KnitErp.Domain.Common;

/// <summary>
/// Сумма прописью для печатных форм: «Шестьдесят одна тысяча рублей 00 копеек». Рубли (RUB), тенге (KZT), сумы (UZS),
/// белорусские рубли (BYN); другая валюта — числом с кодом. До 999 миллиардов.
/// </summary>
public static class AmountInWords
{
    private static readonly string[] UnitsMale = ["", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"];
    private static readonly string[] UnitsFemale = ["", "одна", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"];

    private static readonly string[] Teens =
        ["десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать"];

    private static readonly string[] Tens = ["", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто"];

    private static readonly string[] Hundreds = ["", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот"];

    private sealed record Unit(string One, string Few, string Many, bool Female = false);

    private static readonly Dictionary<string, (Unit Major, Unit Minor)> Currencies = new()
    {
        ["RUB"] = (new("рубль", "рубля", "рублей"), new("копейка", "копейки", "копеек", Female: true)),
        ["BYN"] = (new("белорусский рубль", "белорусских рубля", "белорусских рублей"), new("копейка", "копейки", "копеек", Female: true)),
        ["KZT"] = (new("тенге", "тенге", "тенге"), new("тиын", "тиына", "тиынов")),
        ["UZS"] = (new("сум", "сума", "сумов"), new("тийин", "тийина", "тийинов")),
    };

    private static readonly Unit[] Scales =
    [
        new("тысяча", "тысячи", "тысяч", Female: true),
        new("миллион", "миллиона", "миллионов"),
        new("миллиард", "миллиарда", "миллиардов"),
    ];

    public static string Format(decimal amount, string currencyCode)
    {
        var rounded = Money.Round(amount);
        if (!Currencies.TryGetValue(currencyCode, out var currency) || Math.Abs(rounded) >= 1_000_000_000_000m)
        {
            return $"{rounded:0.00} {currencyCode}";
        }

        var abs = Math.Abs(rounded);
        var major = (long)decimal.Truncate(abs);
        var minor = (int)((abs - major) * 100);
        var words = major == 0 ? "ноль" : Number(major, currency.Major.Female);
        var text = $"{(rounded < 0 ? "минус " : string.Empty)}{words} {Plural(major, currency.Major)} {minor:00} {Plural(minor, currency.Minor)}";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>Число прописью (до 999 миллиардов); female — «одна, две» для тысяч и копеек.</summary>
    public static string Number(long value, bool female = false)
    {
        var parts = new List<string>();
        var groups = new List<int>();
        for (var v = value; v > 0; v /= 1000)
        {
            groups.Add((int)(v % 1000));
        }

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var g = groups[i];
            if (g == 0)
            {
                continue;
            }

            var scale = i == 0 ? null : Scales[i - 1];
            parts.Add(Triad(g, i == 0 ? female : scale!.Female));
            if (scale is not null)
            {
                parts.Add(Plural(g, scale));
            }
        }

        return string.Join(' ', parts.Where(p => p.Length > 0));
    }

    private static string Triad(int n, bool female)
    {
        var words = new List<string> { Hundreds[n / 100] };
        var rest = n % 100;
        if (rest is >= 10 and < 20)
        {
            words.Add(Teens[rest - 10]);
        }
        else
        {
            words.Add(Tens[rest / 10]);
            words.Add((female ? UnitsFemale : UnitsMale)[rest % 10]);
        }

        return string.Join(' ', words.Where(w => w.Length > 0));
    }

    private static string Plural(long n, Unit unit)
    {
        var lastTwo = n % 100;
        var last = n % 10;
        return lastTwo is >= 11 and <= 14 ? unit.Many : last == 1 ? unit.One : last is >= 2 and <= 4 ? unit.Few : unit.Many;
    }
}
