using KnitErp.Application;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Infrastructure;
using KnitErp.Infrastructure.Persistence;
using KnitErp.Web.Components;
using KnitErp.Web.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddKnitErpApplication();
builder.Services.AddKnitErpInfrastructure(
    builder.Configuration.GetConnectionString("KnitErp")
    ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:KnitErp."));

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<ICurrentUser, DevelopmentCurrentUser>();
}
else
{
    builder.Services.AddScoped<ICurrentUser, AnonymousCurrentUser>();
}

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
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/health");
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
        await orgs.CreateWithOwnerAsync(new CreateOrganizationCommand(
            "Общество с ограниченной ответственностью «Солвер»", "ООО «Солвер»", "9705239429", "770501001",
            KppVerified: false, "Europe/Moscow", "owner@kniterp.local", "Владелец (разработка)"));
    }
}

public partial class Program;
