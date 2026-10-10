using KnitErp.Application.Access;
using KnitErp.Application.Workspace;
using KnitErp.Domain.Access;
using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;

namespace KnitErp.Application.Common;

/// <summary>Реквизиты из ЕГРЮЛ/ЕГРИП по ИНН. Active = false — организация ликвидирована или ликвидируется (Status — как в реестре).</summary>
public sealed record CompanyRequisites(
    bool SoleProprietor, string FullName, string ShortName, string Inn, string? Kpp, string? Ogrn, string? Address,
    string? DirectorPosition, string? DirectorName, bool Active, string? Status);

/// <summary>Справочник реквизитов по ИНН (внешний сервис). Без ключа не настроен — кнопка «Заполнить по ИНН» не показывается.</summary>
public interface IRequisitesLookup
{
    bool IsConfigured { get; }

    /// <summary>null — по ИНН ничего нет; недоступность сервиса — <see cref="ExternalServiceUnavailableException"/>.</summary>
    Task<CompanyRequisites?> FindByInnAsync(string inn, CancellationToken ct = default);
}

/// <summary>
/// Заполнение реквизитов по ИНН (D78), как в МойСклад: своего юрлица — с правом «Организация: изменение», контрагента — с правом
/// изменять справочники. Только российский ИНН с верной контрольной суммой; ответ реестра — подсказка, пользователь проверяет и сохраняет сам.
/// </summary>
public sealed class RequisitesLookupService(IRequisitesLookup lookup, IAccessGuard guard)
{
    public bool IsConfigured => lookup.IsConfigured;

    public async Task<CompanyRequisites> ForLegalEntityAsync(string? inn, CancellationToken ct = default)
    {
        await guard.DemandAsync(Permissions.OrganizationEdit, ct);
        return await FindAsync(inn, ct);
    }

    public async Task<CompanyRequisites> ForCounterpartyAsync(string? inn, CancellationToken ct = default)
    {
        await guard.DemandAsync(Permissions.CatalogEdit, ct);
        return await FindAsync(inn, ct);
    }

    private async Task<CompanyRequisites> FindAsync(string? inn, CancellationToken ct)
    {
        var value = (inn ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        if (!RussianRequisites.IsValidLegalEntityInn(value) && !RussianRequisites.IsValidPersonInn(value))
        {
            throw new BusinessRuleException("requisites.inn", "Введите ИНН: 10 цифр у организации или 12 у ИП, с верной контрольной суммой.");
        }

        if (!lookup.IsConfigured)
        {
            throw new BusinessRuleException("requisites.not_configured", "Заполнение по ИНН не подключено: администратору нужно указать ключ DaData в настройках сервера.");
        }

        try
        {
            return await lookup.FindByInnAsync(value, ct)
                   ?? throw new BusinessRuleException("requisites.not_found", $"По ИНН {value} в ЕГРЮЛ/ЕГРИП ничего не найдено.");
        }
        catch (ExternalServiceUnavailableException)
        {
            throw new BusinessRuleException("requisites.unavailable", "Справочник ЕГРЮЛ сейчас недоступен — заполните реквизиты вручную или попробуйте позже.");
        }
    }
}
