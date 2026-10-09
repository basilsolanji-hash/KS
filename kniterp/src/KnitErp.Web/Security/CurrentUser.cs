using KnitErp.Application.Common;

namespace KnitErp.Web.Security;

/// <summary>
/// Временный пользователь только для среды Development, пока не подключён вход через ASP.NET Core Identity.
/// В других средах приложение не выдаёт никаких прав (запрет по умолчанию).
/// </summary>
public sealed class DevelopmentCurrentUser(IConfiguration configuration) : ICurrentUser
{
    public long? UserId => configuration.GetValue<long?>("Dev:UserId");
    public long? OrganizationId => configuration.GetValue<long?>("Dev:OrganizationId");
    public string? CorrelationId { get; } = Guid.NewGuid().ToString("N");
}

/// <summary>Пользователь не определён: любое действие получит отказ.</summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public long? UserId => null;
    public long? OrganizationId => null;
    public string? CorrelationId { get; } = Guid.NewGuid().ToString("N");
}
