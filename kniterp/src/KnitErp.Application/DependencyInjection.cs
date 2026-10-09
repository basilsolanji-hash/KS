using KnitErp.Application.Access;
using KnitErp.Application.Audit;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
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
        return services;
    }
}
