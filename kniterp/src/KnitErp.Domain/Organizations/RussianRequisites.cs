namespace KnitErp.Domain.Organizations;

/// <summary>Проверки российских реквизитов юридического лица.</summary>
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
}
