using System.Globalization;
using System.Text.RegularExpressions;
using KnitErp.Domain.Common;

namespace KnitErp.Application.Workspace;

/// <summary>
/// Калькулятор панели быстрого доступа: + − × ÷, скобки, десятичная запятая и проценты как на бухгалтерском
/// калькуляторе: «1250 + 20%» = 1500, «1250 − 20%» = 1000, «400 × 15%» = 60, «15% от 400» = 60.
/// Считает в decimal — без ошибок округления двоичной дроби. Код не выполняется, только разбирается.
/// </summary>
public static partial class Calculator
{
    public const int MaxLength = 200;

    public static decimal Evaluate(string? expression)
    {
        var text = Normalize(expression);
        if (text.Length == 0)
        {
            throw Error("Введите выражение, например 1250 + 20%.");
        }

        var parser = new Parser(text);
        var (value, _) = parser.ParseExpression();
        if (!parser.AtEnd)
        {
            throw Error($"Непонятный символ «{parser.Current}».");
        }

        return value;
    }

    public static string Format(decimal value) =>
        decimal.Round(value, 10).ToString("#,0.##########", CultureInfo.GetCultureInfo("ru-RU")).Replace(' ', ' ');

    private static string Normalize(string? expression)
    {
        var text = (expression ?? string.Empty).Trim();
        if (text.Length > MaxLength)
        {
            throw Error("Слишком длинное выражение.");
        }

        text = PercentOf().Replace(text.ToLowerInvariant(), "($1/100)*");
        return text.Replace('×', '*').Replace('·', '*').Replace('÷', '/').Replace(':', '/').Replace('−', '-')
            .Replace(',', '.').Replace(" ", "").Replace(" ", "");
    }

    private static BusinessRuleException Error(string message) => new("calculator.invalid", message);

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*%\s*(?:от|of)\s*")]
    private static partial Regex PercentOf();

    private sealed class Parser(string text)
    {
        private int _pos;

        public bool AtEnd => _pos >= text.Length;
        public char Current => text[_pos];

        /// <summary>Сумма и разность. Процент справа от + или − берётся от левой части.</summary>
        public (decimal Value, bool IsPercent) ParseExpression()
        {
            var (left, leftPercent) = ParseTerm();
            while (!AtEnd && Current is '+' or '-')
            {
                var op = text[_pos++];
                var (right, rightPercent) = ParseTerm();
                var delta = rightPercent ? left * right / 100 : right;
                left = op == '+' ? left + delta : left - delta;
                leftPercent = false;
            }

            return (leftPercent ? left / 100 : left, false);
        }

        /// <summary>Произведение и частное. Процент как множитель — доля: 400 × 15% = 60.</summary>
        private (decimal Value, bool IsPercent) ParseTerm()
        {
            var (left, leftPercent) = ParseFactor();
            while (!AtEnd && Current is '*' or '/')
            {
                var op = text[_pos++];
                var (right, rightPercent) = ParseFactor();
                if (leftPercent)
                {
                    left /= 100;
                    leftPercent = false;
                }

                var r = rightPercent ? right / 100 : right;
                if (op == '/' && r == 0)
                {
                    throw Error("Деление на ноль.");
                }

                try
                {
                    left = op == '*' ? left * r : left / r;
                }
                catch (OverflowException)
                {
                    throw Error("Слишком большое число.");
                }
            }

            return (left, leftPercent);
        }

        private (decimal Value, bool IsPercent) ParseFactor()
        {
            if (AtEnd)
            {
                throw Error("Выражение не закончено.");
            }

            if (Current == '-')
            {
                _pos++;
                var (v, p) = ParseFactor();
                return (-v, p);
            }

            if (Current == '+')
            {
                _pos++;
                return ParseFactor();
            }

            decimal value;
            if (Current == '(')
            {
                _pos++;
                (value, _) = ParseExpression();
                if (AtEnd || Current != ')')
                {
                    throw Error("Не хватает закрывающей скобки.");
                }

                _pos++;
            }
            else
            {
                var start = _pos;
                while (!AtEnd && (char.IsDigit(Current) || Current == '.'))
                {
                    _pos++;
                }

                if (start == _pos || !decimal.TryParse(text[start.._pos], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                {
                    throw Error(AtEnd ? "Выражение не закончено." : $"Непонятный символ «{Current}».");
                }
            }

            if (!AtEnd && Current == '%')
            {
                _pos++;
                return (value, true);
            }

            return (value, false);
        }
    }
}
