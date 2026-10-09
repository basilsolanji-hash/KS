using KnitErp.Application;
using KnitErp.Application.Authentication;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Infrastructure;
using KnitErp.Infrastructure.Persistence;
using KnitErp.Web.Components;
using KnitErp.Web.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddKnitErpApplication();
builder.Services.AddKnitErpInfrastructure(
    builder.Configuration.GetConnectionString("KnitErp")
    ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:KnitErp."));

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SessionRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<ICurrentUser, ClaimsCurrentUser>();

builder.Services.AddAuthentication(AuthSchemes.Session)
    .AddCookie(AuthSchemes.Session, o =>
    {
        o.Cookie.Name = "knitERP.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/account/login";
        o.AccessDeniedPath = "/account/login";
        // Допущение D27: сессия живёт 12 часов (рабочая смена) и продлевается при активности.
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = SessionValidation.OnValidatePrincipal;
    })
    .AddCookie(AuthSchemes.Pending, o =>
    {
        o.Cookie.Name = "knitERP.pending";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        o.SlidingExpiration = false;
    });

// Запрет по умолчанию: любая страница требует входа, кроме явно открытых страниц входа.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await SeedDevelopmentAsync(app);
}
else
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

app.MapPost("/account/logout", async (HttpContext http, IAntiforgery antiforgery, SignInService signIn) =>
{
    if (!await antiforgery.IsRequestValidAsync(http))
    {
        return Results.BadRequest();
    }

    if (SessionClaims.Read(http.User) is { } session)
    {
        await signIn.RecordSignOutAsync(session.UserId, session.OrganizationId, http.RequestAborted);
    }

    await http.SignOutAsync(AuthSchemes.Session);
    await http.SignOutAsync(AuthSchemes.Pending);
    return Results.LocalRedirect("~/account/login");
}).AllowAnonymous();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

// Тестовая организация для локальной разработки. Реальные данные сюда не попадают.
static async Task SeedDevelopmentAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KnitErpDbContext>();
    await KnitErp.Infrastructure.DependencyInjection.EnsureDatabaseAsync(db);
    if (!await db.Organizations.AnyAsync())
    {
        var orgs = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var created = await orgs.CreateWithOwnerAsync(new CreateOrganizationCommand(
            "Общество с ограниченной ответственностью «Солвер»", "ООО «Солвер»", "9705239429", "770501001",
            KppVerified: false, "Europe/Moscow", "owner@kniterp.local", "Владелец (разработка)"));

        // Только для разработки: ссылка установки пароля Владельца выводится в лог локального запуска.
        app.Logger.LogWarning("Пароль Владельца не задан. Откройте /account/invite?token={Token}", created.OwnerSetupToken);
    }
}

public partial class Program;
