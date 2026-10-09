using System.Security.Claims;
using KnitErp.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace KnitErp.Web.Security;

public static class AuthSchemes
{
    /// <summary>Сессия вошедшего пользователя.</summary>
    public const string Session = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Короткое состояние «пароль проверен, ждём второй фактор». Доступа к данным не даёт.</summary>
    public const string Pending = "KnitErp.Pending";

    public const string PendingUserClaim = "kniterp:pending-user";
}

/// <summary>Сохранение состояния между паролем и кодом 2FA в подписанной cookie на 5 минут.</summary>
public static class PendingSignInCookie
{
    public static Task SignInAsync(HttpContext http, PendingSignIn pending) =>
        http.SignInAsync(AuthSchemes.Pending, new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AuthSchemes.PendingUserClaim, pending.UserId.ToString()),
            new Claim(SessionClaims.StampClaim, pending.SecurityStamp.ToString("N")),
        ], AuthSchemes.Pending)));

    public static async Task<PendingSignIn?> ReadAsync(HttpContext http)
    {
        var result = await http.AuthenticateAsync(AuthSchemes.Pending);
        if (!result.Succeeded
            || !long.TryParse(result.Principal.FindFirstValue(AuthSchemes.PendingUserClaim), out var userId)
            || SessionClaims.Stamp(result.Principal) is not { } stamp)
        {
            return null;
        }

        return new PendingSignIn(userId, stamp);
    }

    /// <summary>Полный вход: сессия выдаётся, промежуточное состояние удаляется.</summary>
    public static async Task CompleteAsync(HttpContext http, SessionIdentity session)
    {
        await http.SignOutAsync(AuthSchemes.Pending);
        await http.SignInAsync(AuthSchemes.Session, SessionClaims.Create(session));
    }
}

/// <summary>
/// Проверка cookie на HTTP-запросах: не реже раза в минуту сверяем штамп, статус пользователя и участие в организации.
/// Блокировка и смена прав закрывают сессию не позднее чем через 60 секунд.
/// </summary>
public static class SessionValidation
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private const string ValidatedAtKey = "kniterp.validated";

    public static async Task OnValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var now = DateTimeOffset.UtcNow;
        if (context.Properties.Items.TryGetValue(ValidatedAtKey, out var raw)
            && DateTimeOffset.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var validatedAt)
            && now - validatedAt < Interval)
        {
            return;
        }

        var session = context.Principal is null ? null : SessionClaims.Read(context.Principal);
        var signIn = context.HttpContext.RequestServices.GetRequiredService<SignInService>();
        if (session is null
            || !await signIn.ValidateSessionAsync(session.UserId, session.OrganizationId, session.SecurityStamp, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSchemes.Session);
            return;
        }

        context.Properties.Items[ValidatedAtKey] = now.ToString("O");
        context.ShouldRenew = true;
    }
}

/// <summary>Та же проверка для открытой вкладки Blazor: цепь живёт долго, поэтому сверяемся раз в минуту.</summary>
public sealed class SessionRevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => SessionValidation.Interval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        if (SessionClaims.Read(state.User) is not { } session)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
        return await signIn.ValidateSessionAsync(session.UserId, session.OrganizationId, session.SecurityStamp, cancellationToken);
    }
}
