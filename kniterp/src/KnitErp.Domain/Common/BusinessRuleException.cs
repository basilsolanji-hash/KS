namespace KnitErp.Domain.Common;

/// <summary>
/// Нарушение бизнес-правила. Код стабилен и используется в тестах и API, текст показывается пользователю.
/// </summary>
public sealed class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
