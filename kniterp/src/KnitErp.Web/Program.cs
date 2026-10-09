using KnitErp.Application;
using KnitErp.Application.Authentication;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Warehousing;
using KnitErp.Infrastructure;
using KnitErp.Infrastructure.Persistence;
using KnitErp.Infrastructure.Security;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using KnitErp.Web.Components;
using KnitErp.Web.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

// «dotnet KnitErp.Web.dll new-master-key» — создать мастер-ключ для секретов сервера и выйти. Ключ печатается один раз.
if (args.Contains("new-master-key", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(KeyRing.NewKeyBase64());
    return;
}

// Команды сервера с параметрами (их параметры не передаются в конфигурацию приложения):
// «create-organization --inn …» — создать рабочую организацию с Владельцем (docs/pilot.md);
// «emergency-access --email … --reason …» — аварийно восстановить вход пользователю (D06, docs/operations.md).
var provisioning = Array.FindIndex(args, a => a.Equals(OrganizationProvisioning.CommandName, StringComparison.OrdinalIgnoreCase)
                                              || a.Equals(EmergencyCommand.Name, StringComparison.OrdinalIgnoreCase));
var builder = WebApplication.CreateBuilder(provisioning >= 0 ? args[..provisioning] : args);

// Мастер-ключ: шифрование секретов 2FA и ключей cookie, подпись журнала аудита и движений склада.
// Хранится в секретах сервера, не в базе и не в репозитории; рабочая среда без него не запускается.
var masterKey = SecuritySetup.LoadMasterKey(
    builder.Configuration["Security:MasterKey"], builder.Configuration["Security:PreviousMasterKeys"], builder.Environment.IsDevelopment());
KeyRing.Configure(masterKey.Ring);

var connectionString = builder.Configuration.GetConnectionString("KnitErp")
                       ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:KnitErp.");
if (!builder.Environment.IsDevelopment())
{
    SecuritySetup.EnsureEncryptedConnection(connectionString, builder.Configuration.GetValue<bool>("Security:AllowUnencryptedDatabase"));
}

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddKnitErpApplication();
builder.Services.AddKnitErpInfrastructure(
    connectionString,
    // ИИ-помощник включается ключом из секретов сервера; без ключа он отвечает по справочному центру.
    builder.Configuration["Assistant:ApiKey"] ?? builder.Configuration["ANTHROPIC_API_KEY"],
    builder.Configuration["Assistant:Model"]);

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

// За обратным прокси (Caddy на сервере, deploy/beget): адрес клиента и HTTPS берутся из заголовков X-Forwarded-*,
// но только от прокси из доверенной сети — иначе любой мог бы подставить чужой адрес и обойти ограничение попыток входа.
var proxyNetwork = builder.Configuration["ReverseProxy:KnownNetwork"];
if (!string.IsNullOrWhiteSpace(proxyNetwork))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxyNetwork));
        o.ForwardLimit = 1;
    });
}

builder.Services.AddScoped<KnitErp.Web.Exports.ReportExports>();
builder.Services.AddHealthChecks().AddCheck<KnitErp.Web.Security.DatabaseHealthCheck>("database");

// Ключи cookie и антиподделки — в базе, зашифрованные мастер-ключом: сессии переживают перезапуск и несколько серверов,
// а копия базы ключей не раскрывает.
builder.Services.AddDataProtection()
    .SetApplicationName("knitERP")
    .PersistKeysToDbContext<KnitErpDbContext>()
    .AddKeyManagementOptions(o => o.XmlEncryptor = new MasterKeyXmlEncryptor());

// Подбор паролей и кодов: не больше 10 отправок форм входа в минуту с одного адреса (плюс блокировка учётной записи D25).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        HttpMethods.IsPost(http.Request.Method) && http.Request.Path.StartsWithSegments("/account")
                                                && !http.Request.Path.StartsWithSegments("/account/logout")
            ? RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other"));
    o.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync("Слишком много попыток входа с этого адреса. Подождите минуту и попробуйте снова.", ct);
    };
});

var app = builder.Build();
app.Logger.LogInformation("Защита данных: {Source}", masterKey.Description);

if (provisioning >= 0)
{
    Environment.ExitCode = args[provisioning].Equals(EmergencyCommand.Name, StringComparison.OrdinalIgnoreCase)
        ? await EmergencyAccessAsync(app, args[(provisioning + 1)..])
        : await CreateOrganizationAsync(app, args[(provisioning + 1)..]);
    return;
}

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

if (!string.IsNullOrWhiteSpace(proxyNetwork))
{
    // За прокси на HTTPS переводит сам прокси; приложение слушает только внутренний HTTP.
    app.UseForwardedHeaders();
}
else
{
    app.UseHttpsRedirection();
}
app.Use(SecurityHeaders.Apply);
app.UseRateLimiter();
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
app.MapGet("/counterparties.xlsx", (CounterpartyExchangeService exchange, CancellationToken ct) =>
    FileOrForbid(() => exchange.ExportAsync(ct), $"kontragenty-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"));
app.MapGet("/counterparties-template.xlsx", (CounterpartyExchangeService exchange, CancellationToken ct) =>
    FileOrForbid(() => exchange.TemplateAsync(ct), "shablon-kontragentov.xlsx"));
app.MapGet("/employees.xlsx", (KnitErp.Application.Structure.EmployeeExchangeService exchange, CancellationToken ct) =>
    FileOrForbid(() => exchange.ExportAsync(ct), $"sotrudniki-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"));
app.MapGet("/employees-template.xlsx", (KnitErp.Application.Structure.EmployeeExchangeService exchange, CancellationToken ct) =>
    FileOrForbid(() => exchange.TemplateAsync(ct), "shablon-sotrudnikov.xlsx"));

app.MapGet("/opening-balances/template.xlsx", (OpeningBalanceService balances, CancellationToken ct) =>
    FileOrForbid(() => balances.LinesTemplateAsync(ct), "shablon-nachalnyh-ostatkov.xlsx"));
app.MapGet("/stock-documents/template.xlsx", (StockDocumentService documents, CancellationToken ct) =>
    FileOrForbid(() => documents.LinesTemplateAsync(ct), "shablon-strok-dokumenta.xlsx"));
// Отчёты и журналы в Excel — те же фильтры, что на экране; права и область складов проверяют сервисы.
app.MapGet("/reports/stock.xlsx", (long? warehouse, byte? type, string? search, KnitErp.Web.Exports.ReportExports export, CancellationToken ct) =>
    FileOrForbid(() => export.StockAsync(new StockFilter(warehouse, (KnitErp.Domain.Catalog.ItemType?)type, search), ct),
        $"ostatki-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"));
app.MapGet("/reports/movements.xlsx", (long? warehouse, DateOnly? from, DateOnly? to, string? search, KnitErp.Web.Exports.ReportExports export, CancellationToken ct) =>
    FileOrForbid(() => export.MovementsAsync(new MovementFilter(warehouse, from, to, search), ct), $"dvizheniya-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"));
app.MapGet("/reports/turnover.xlsx", (long? warehouse, DateOnly from, DateOnly to, byte? type, string? search, KnitErp.Web.Exports.ReportExports export, CancellationToken ct) =>
    FileOrForbid(() => export.TurnoverAsync(new TurnoverFilter(warehouse, from, to, (KnitErp.Domain.Catalog.ItemType?)type, search), ct),
        $"oboroty-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx"));
app.MapGet("/audit.xlsx", (KnitErp.Web.Exports.ReportExports export, CancellationToken ct) =>
    FileOrForbid(() => export.AuditAsync(ct), $"zhurnal-audita-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"));
app.MapGet("/signins.xlsx", (DateOnly? from, DateOnly? to, bool? failures, KnitErp.Web.Exports.ReportExports export, CancellationToken ct) =>
    FileOrForbid(() => export.SignInsAsync(from, to, failures == true, ct), $"zhurnal-vhodov-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"));
app.MapGet("/inventory/{id:long}/sheet.xlsx", (long id, InventoryService inventory, CancellationToken ct) =>
    FileOrForbid(() => inventory.CountSheetAsync(id, ct), $"blank-inventarizacii-{id}.xlsx"));

// Встраивать страницы knitERP в чужие сайты запрещено (как и в общей CSP).
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = "'none'");

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
    catch (NotFoundException)
    {
        return Results.NotFound();
    }
    catch (KnitErp.Domain.Common.BusinessRuleException ex)
    {
        return Results.BadRequest(ex.Message);
    }
}

static async Task MigrateAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KnitErpDbContext>();
    var pending = await KnitErp.Infrastructure.DependencyInjection.PendingMigrationsAsync(db);
    await KnitErp.Infrastructure.DependencyInjection.MigrateDatabaseAsync(db);
    app.Logger.LogInformation("Применено миграций: {Count} ({Names})", pending.Count, string.Join(", ", pending));
    var encrypted = await SecuritySetup.EncryptLegacySecretsAsync(db);
    if (encrypted > 0)
    {
        app.Logger.LogInformation("Зашифровано секретов 2FA, записанных до включения шифрования: {Count}", encrypted);
    }
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

// Создание рабочей организации на сервере. Ссылка установки пароля Владельца печатается один раз и в журнал не пишется:
// её нужно передать Владельцу лично. Повторный запуск с тем же ИНН ничего не создаёт.
static async Task<int> CreateOrganizationAsync(WebApplication app, string[] args)
{
    var (request, errors) = OrganizationProvisioning.Parse(args);
    if (request is null)
    {
        foreach (var e in errors)
        {
            Console.Error.WriteLine(e);
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Пример:");
        foreach (var line in OrganizationProvisioning.Usage)
        {
            Console.Error.WriteLine(line);
        }

        return 2;
    }

    await EnsureSchemaIsCurrentAsync(app);
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KnitErpDbContext>();
    var inn = request.Command.Inn.Trim();
    if (await db.Organizations.AnyAsync(o => o.CountryCode == request.Command.CountryCode && o.Inn == inn))
    {
        Console.Error.WriteLine($"Организация с ИНН {inn} уже есть. Ничего не создано.");
        return 1;
    }

    try
    {
        var created = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateWithOwnerAsync(request.Command);
        app.Logger.LogInformation("Создана организация {OrganizationId} с Владельцем {UserId}", created.OrganizationId, created.OwnerUserId);
        Console.WriteLine($"Создана организация: {request.Command.ShortName} (№ {created.OrganizationId}).");
        if (created.OwnerSetupToken is { } token)
        {
            Console.WriteLine("Ссылка для Владельца — установить пароль и подключить вход с кодом (действует 72 часа, показывается один раз):");
            Console.WriteLine(OrganizationProvisioning.SetupLink(request.BaseUrl, token));
        }
        else
        {
            Console.WriteLine("У Владельца уже есть пароль: организация появится у него в списке после входа.");
        }

        return 0;
    }
    catch (KnitErp.Domain.Common.BusinessRuleException ex)
    {
        Console.Error.WriteLine($"Не создано: {ex.Message}");
        return 1;
    }
}

// Аварийное восстановление входа (D06): сброс 2FA и резервных кодов, ссылка нового пароля. Ссылка печатается один раз.
static async Task<int> EmergencyAccessAsync(WebApplication app, string[] args)
{
    var (values, errors) = EmergencyCommand.Parse(args);
    if (values is null)
    {
        foreach (var e in errors)
        {
            Console.Error.WriteLine(e);
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(EmergencyCommand.Usage);
        return 2;
    }

    await EnsureSchemaIsCurrentAsync(app);
    using var scope = app.Services.CreateScope();
    try
    {
        var result = await scope.ServiceProvider.GetRequiredService<KnitErp.Application.Authentication.RecoveryCodeService>()
            .EmergencyRestoreAsync(values.Email, values.Reason);
        app.Logger.LogWarning("Аварийное восстановление входа пользователю {UserId}", result.UserId);
        Console.WriteLine($"Вход с кодом (2FA) и резервные коды пользователя «{result.DisplayName}» сброшены, блокировка входа снята.");
        Console.WriteLine("Прежний пароль действует. Ссылка для нового пароля (72 часа, показывается один раз):");
        Console.WriteLine(OrganizationProvisioning.SetupLink(values.BaseUrl, result.RecoveryToken));
        Console.WriteLine("При следующем входе Владелец и Администратор подключат приложение-аутентификатор заново и выпустят новые резервные коды.");
        return 0;
    }
    catch (NotFoundException)
    {
        Console.Error.WriteLine($"Пользователь {values.Email} не найден. Ничего не изменено.");
        return 1;
    }
    catch (KnitErp.Domain.Common.BusinessRuleException ex)
    {
        Console.Error.WriteLine($"Не выполнено: {ex.Message}");
        return 1;
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
            KppVerified: true, "Europe/Moscow", "owner@kniterp.local", "Владелец (разработка)"));

        // Только для разработки: ссылка установки пароля Владельца выводится в лог локального запуска.
        app.Logger.LogWarning("Пароль Владельца не задан. Откройте /account/invite?token={Token}", created.OwnerSetupToken);
    }
}

public partial class Program;
