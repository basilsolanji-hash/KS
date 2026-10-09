using KnitErp.Application.Common;
using KnitErp.Domain.Common;

namespace KnitErp.Web.Components.Shared;

/// <summary>Ошибки, которые показываются пользователю как есть. Остальные — сбой, их видит только лог.</summary>
public static class UiErrors
{
    public static bool IsUserFacing(Exception ex) =>
        ex is BusinessRuleException or AccessDeniedException or NotFoundException or ConcurrencyConflictException;

    public static string Message(Exception ex) => ex.Message;
}
