namespace KnitErp.Domain.Common;

/// <summary>Проверка обязательных и необязательных текстовых полей справочников.</summary>
internal static class DomainText
{
    public static string Require(string? value, int maxLength, string field)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            throw new BusinessRuleException("field.required", $"Поле «{field}» обязательно.");
        }

        if (v.Length > maxLength)
        {
            throw new BusinessRuleException("field.too_long", $"Поле «{field}» длиннее {maxLength} символов.");
        }

        return v;
    }

    public static string? Optional(string? value, int maxLength, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : Require(value, maxLength, field);
}
