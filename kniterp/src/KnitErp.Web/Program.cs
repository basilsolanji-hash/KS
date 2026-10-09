using KnitErp.Application;
using KnitErp.Application.Authentication;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Warehousing;
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

// «dotnet KnitErp.Web.dll migrate» — отдельный шаг выпуска: применить миграции и выйти.
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    await MigrateAsync(app);
    return;
}

if (app.Environment.IsDevelopment())
{
    await MigrateAsync(app);
    await SeedDevelopmentAsync(app);
}
else
{
    await EnsureSchemaIsCurrentAsync(app);
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

// Файлы Excel отдаются обычными ссылками: права проверяет сервис, отказ — 403 и запись в журнал.
app.MapGet("/catalog/items.xlsx", (ItemExchangeService exchange, CancellationToken ct) =>
    FileOrForbid(() => exchange.ExportAsync(ct), $"nomenklatura-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"));
app.MapGet("/catalog/items-template.xlsx", (ItemExchangeService exchange, CancellationToken ct) =>
    FileOrForbid(() => exchange.TemplateAsync(ct), "shablon-nomenklatury.xlsx"));

app.MapGet("/opening-balances/template.xlsx", (OpeningBalanceService balances, CancellationToken ct) =>
    FileOrForbid(() => balances.LinesTemplateAsync(ct), "shablon-nachalnyh-ostatkov.xlsx"));
app.MapGet("/stock-documents/template.xlsx", (StockDocumentService documents, CancellationToken ct) =>
    FileOrForbid(() => documents.LinesTemplateAsync(ct), "shablon-strok-dokumenta.xlsx"));

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

static async Task<IResult> FileOrForbid(Func<Task<byte[]>> build, string fileName)
{
    try
    {
        return Results.File(await build(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
    catch (AccessDeniedException)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}

static async Task MigrateAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KnitErpDbContext>();
    var pending = await KnitErp.Infrastructure.DependencyInjection.PendingMigrationsAsync(db);
    await KnitErp.Infrastructure.DependencyInjection.MigrateDatabaseAsync(db);
    app.Logger.LogInformation("Применено миграций: {Count} ({Names})", pending.Count, string.Join(", ", pending));
}

// Рабочая среда сама схему не меняет: при неприменённых миграциях приложение не запускается,
// чтобы новая версия кода не работала со старой схемой.
static async Task EnsureSchemaIsCurrentAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KnitErpDbContext>();
    var pending = await KnitErp.Infrastructure.DependencyInjection.PendingMigrationsAsync(db);
    if (pending.Count > 0)
    {
        throw new InvalidOperationException(
            $"Схема базы устарела, неприменённые миграции: {string.Join(", ", pending)}. Выполните «dotnet KnitErp.Web.dll migrate».");
    }
}

// Тестовая организация для локальной разработки. Реальные данные сюда не попадают.
static async Task SeedDevelopmentAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KnitErpDbContext>();
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
