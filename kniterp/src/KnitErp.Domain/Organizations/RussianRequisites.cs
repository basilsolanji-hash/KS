namespace KnitErp.Domain.Organizations;

/// <summary>Проверки российских реквизитов: ИНН юрлица и ИП, КПП, БИК и банковские счета.</summary>
public static class RussianRequisites
{
    private static readonly int[] InnLegalWeights = [2, 4, 10, 3, 5, 9, 4, 6, 8];

    /// <summary>ИНН юридического лица: 10 цифр, последняя — контрольная.</summary>
    public static bool IsValidLegalEntityInn(string? inn)
    {
        if (inn is null || inn.Length != 10 || !inn.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (inn[i] - '0') * InnLegalWeights[i];
        }

        return sum % 11 % 10 == inn[9] - '0';
    }

    private static readonly int[] InnPersonWeights11 = [7, 2, 4, 10, 3, 5, 9, 4, 6, 8];
    private static readonly int[] InnPersonWeights12 = [3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8];

    /// <summary>ИНН физического лица или ИП: 12 цифр, две последние — контрольные.</summary>
    public static bool IsValidPersonInn(string? inn)
    {
        if (inn is null || inn.Length != 12 || !inn.All(char.IsAsciiDigit))
        {
            return false;
        }

        static int Check(string s, int[] weights)
        {
            var sum = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                sum += (s[i] - '0') * weights[i];
            }

            return sum % 11 % 10;
        }

        return Check(inn, InnPersonWeights11) == inn[10] - '0' && Check(inn, InnPersonWeights12) == inn[11] - '0';
    }

    /// <summary>КПП: 9 символов, 5-й и 6-й могут быть буквами A–Z.</summary>
    public static bool IsValidKpp(string? kpp)
    {
        if (kpp is null || kpp.Length != 9)
        {
            return false;
        }

        for (var i = 0; i < 9; i++)
        {
            var c = kpp[i];
            var ok = (i is 4 or 5) ? char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c) : char.IsAsciiDigit(c);
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>БИК банка: 9 цифр, начинается с 04.</summary>
    public static bool IsValidBik(string? bik) => bik is { Length: 9 } && bik.All(char.IsAsciiDigit) && bik.StartsWith("04", StringComparison.Ordinal);

    /// <summary>Расчётный счёт: 20 цифр, контрольный ключ — вместе с тремя последними цифрами БИК.</summary>
    public static bool IsValidSettlementAccount(string? account, string bik) => IsValidBik(bik) && AccountKey(bik[^3..], account);

    /// <summary>Корреспондентский счёт: 20 цифр, 301…, контрольный ключ — вместе с «0» и 5–6-й цифрами БИК.</summary>
    public static bool IsValidCorrespondentAccount(string? account, string bik) =>
        IsValidBik(bik) && account is not null && account.StartsWith("301", StringComparison.Ordinal) && AccountKey("0" + bik[4..6], account);

    private static bool AccountKey(string prefix, string? account)
    {
        if (account is null || account.Length != 20 || !account.All(char.IsAsciiDigit))
        {
            return false;
        }

        var s = prefix + account;
        int[] weights = [7, 1, 3];
        var sum = 0;
        for (var i = 0; i < s.Length; i++)
        {
            sum += (s[i] - '0') * weights[i % 3] % 10;
        }

        return sum % 10 == 0;
    }
}
