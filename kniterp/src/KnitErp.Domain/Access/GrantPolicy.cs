using KnitErp.Domain.Common;

namespace KnitErp.Domain.Access;

/// <summary>Проверки выдачи прав (ТЗ §4.12 KA3657, приёмка §4.14 «д»).</summary>
public static class GrantPolicy
{
    /// <summary>
    /// Нельзя выдать право, которого нет у самого выдающего. Права «П» и «С» в роли не дают доступа и не проверяются.
    /// </summary>
    public static void EnsureNoEscalation(EffectivePermissionSet grantor, IEnumerable<(string PermissionCode, PermissionLevel Level)> granted)
    {
        // Допущение D23: у Владельца (право выдачи административных прав) нет части прав из-за разделения обязанностей
        // (например, создание начальных остатков), но он должен назначать любые роли организации.
        if (grantor.Has(Permissions.AdminGrant))
        {
            return;
        }

        foreach (var (code, level) in granted)
        {
            if (!level.Grants())
            {
                continue;
            }

            if (!grantor.Has(code))
            {
                throw new BusinessRuleException(
                    "access.escalation",
                    $"Нельзя выдать право выше своего: «{Permissions.Describe(code)}».");
            }
        }
    }

    /// <summary>Выдача административных прав — только Владелец (ТЗ §4.8 KA3634).</summary>
    public static void EnsureCanGrantAdministrative(EffectivePermissionSet grantor, bool isAdministrative)
    {
        if (isAdministrative && !grantor.Has(Permissions.AdminGrant))
        {
            throw new BusinessRuleException(
                "access.admin_grant_denied",
                "Выдача административных прав — только Владелец организации.");
        }
    }

    public static void EnsureReason(bool required, string? reason)
    {
        if (required && string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException(
                "access.reason.required",
                "Причина обязательна при выдаче административных прав и при запрете.");
        }
    }

    public static void EnsureValidPeriod(DateTime validFromUtc, DateTime? validToUtc)
    {
        if (validToUtc is { } to && to <= validFromUtc)
        {
            throw new BusinessRuleException("access.period.invalid", "Срок окончания должен быть позже начала.");
        }
    }

    /// <summary>Нельзя оставить организацию без активного Владельца.</summary>
    public static void EnsureOwnerRemains(int activeOwnersAfterChange)
    {
        if (activeOwnersAfterChange < 1)
        {
            throw new BusinessRuleException(
                "access.last_owner",
                "Нельзя оставить организацию без активного Владельца.");
        }
    }
}
