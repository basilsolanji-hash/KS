using KnitErp.Application.Access;
using KnitErp.Application.Audit;
using KnitErp.Application.Catalog;
using KnitErp.Application.Authentication;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Structure;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnitErp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddKnitErpApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<IAccessGuard, AccessGuard>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<UserAccessService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<StructureService>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<CounterpartyService>();
        services.AddScoped<OperationReasonService>();
        services.AddScoped<ItemExchangeService>();
        services.AddScoped<OpeningBalanceService>();
        services.AddScoped<StockService>();
        services.TryAddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddScoped<SignInService>();
        return services;
    }
}
