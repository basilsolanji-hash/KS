using KnitErp.Domain.Access;

namespace KnitErp.Application.Common;

/// <summary>Отказ в доступе → HTTP 403 с понятным сообщением (ТЗ §4.12).</summary>
public sealed class AccessDeniedException(string permissionCode)
    : Exception($"Недостаточно прав: {Permissions.Describe(permissionCode)}. Обратитесь к администратору.")
{
    public string PermissionCode { get; } = permissionCode;
}

/// <summary>
/// Объект не найден → HTTP 404. Используется и для данных чужой организации,
/// чтобы не раскрывать их существование (ТЗ §4.12).
/// </summary>
public sealed class NotFoundException(string entity) : Exception($"{entity}: не найдено.")
{
    public string Entity { get; } = entity;
}

/// <summary>Данные изменил другой пользователь → HTTP 409, без тихой перезаписи.</summary>
public sealed class ConcurrencyConflictException()
    : Exception("Данные изменены другим пользователем. Обновите страницу и повторите.");
