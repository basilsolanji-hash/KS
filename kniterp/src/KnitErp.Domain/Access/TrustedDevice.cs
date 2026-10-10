namespace KnitErp.Domain.Access;

/// <summary>
/// «Доверять этому устройству» (D81): после входа с кодом 2FA браузер получает случайный токен в cookie, в базе — только его
/// SHA-256. Следующие входы с этого браузера — по паролю без кода, пока не истёк срок и не сменился штамп безопасности
/// (смена пароля, сброс 2FA, блокировка закрывают и доверенные устройства). Пароль нужен всегда.
/// </summary>
public sealed class TrustedDevice
{
    public const int LabelMaxLength = 200;

    private TrustedDevice()
    {
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];

    /// <summary>Штамп пользователя на момент доверия: сменился — устройство больше не доверенное.</summary>
    public Guid SecurityStamp { get; private set; }

    /// <summary>Браузер и система — чтобы пользователь узнал устройство в списке.</summary>
    public string Label { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? LastUsedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>Новое доверенное устройство: запись для базы и токен для cookie (показывается браузеру один раз).</summary>
    public static (TrustedDevice Device, string Token) Create(long userId, Guid securityStamp, string? label, DateTime nowUtc)
    {
        var token = SetupTokens.Generate();
        var text = (label ?? string.Empty).Trim();
        return (new TrustedDevice
        {
            UserId = userId,
            TokenHash = SetupTokens.Hash(token),
            SecurityStamp = securityStamp,
            Label = text.Length == 0 ? "Неизвестное устройство" : text[..Math.Min(text.Length, LabelMaxLength)],
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc + SignInPolicy.TrustedDeviceLifetime,
        }, token);
    }

    public bool IsActive(Guid securityStamp, DateTime nowUtc) =>
        RevokedAtUtc is null && ExpiresAtUtc > nowUtc && SecurityStamp == securityStamp;

    public void MarkUsed(DateTime nowUtc) => LastUsedAtUtc = nowUtc;

    public void Revoke(DateTime nowUtc) => RevokedAtUtc ??= nowUtc;
}
