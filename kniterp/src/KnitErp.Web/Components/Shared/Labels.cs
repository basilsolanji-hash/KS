using KnitErp.Domain.Audit;

namespace KnitErp.Web.Components.Shared;

public static class AuditLabels
{
    private static readonly Dictionary<string, string> Actions = new(StringComparer.Ordinal)
    {
        [AuditActions.AccessDenied] = "Отказ в доступе",
        [AuditActions.RoleGranted] = "Назначена роль",
        [AuditActions.AdminRoleGranted] = "Назначена административная роль",
        [AuditActions.PermissionGranted] = "Выдано право",
        [AuditActions.AssignmentRevoked] = "Отозвано право",
        [AuditActions.UserInvited] = "Приглашён пользователь",
        [AuditActions.UserBlocked] = "Пользователь заблокирован",
        [AuditActions.UserUnblocked] = "Пользователь разблокирован",
        [AuditActions.InvitationIssued] = "Выдана ссылка приглашения",
        [AuditActions.TwoFactorReset] = "Сброшена 2FA",
        [AuditActions.SignedIn] = "Вход",
        [AuditActions.SignInFailed] = "Неудачный вход",
        [AuditActions.LockedOut] = "Вход заблокирован",
        [AuditActions.SignedOut] = "Выход",
        [AuditActions.InvitationAccepted] = "Установлен пароль",
        [AuditActions.TwoFactorEnabled] = "Подключена 2FA",
        [AuditActions.DepartmentCreated] = "Создано подразделение",
        [AuditActions.DepartmentChanged] = "Изменено подразделение",
        [AuditActions.DepartmentArchived] = "Подразделение в архиве",
        [AuditActions.PositionCreated] = "Создана должность",
        [AuditActions.PositionChanged] = "Изменена должность",
        [AuditActions.PositionArchived] = "Должность в архиве",
        [AuditActions.EmployeeHired] = "Принят сотрудник",
        [AuditActions.EmployeeChanged] = "Изменена карточка сотрудника",
        [AuditActions.EmployeeDismissed] = "Сотрудник уволен",
        [AuditActions.EmployeeLinkedToUser] = "Карточка связана с учётной записью",
        [AuditActions.CatalogCreated] = "Добавлено в справочник",
        [AuditActions.CatalogChanged] = "Изменено в справочнике",
        [AuditActions.CatalogArchived] = "Отправлено в архив",
        [AuditActions.CatalogRestored] = "Возвращено из архива",
        [AuditActions.CatalogImported] = "Импорт номенклатуры",
        [AuditActions.StockDocumentCreated] = "Создан складской документ",
        [AuditActions.StockDocumentChanged] = "Изменён складской документ",
        [AuditActions.StockDocumentSubmitted] = "Документ на утверждении",
        [AuditActions.StockDocumentReturned] = "Документ возвращён на доработку",
        [AuditActions.StockDocumentApproved] = "Документ утверждён",
        [AuditActions.StockDocumentCancelled] = "Документ отменён",
        [AuditActions.StockDocumentPosted] = "Документ проведён",
        [AuditActions.StockDocumentReversed] = "Документ сторнирован",
        [AuditActions.PeriodClosed] = "Период закрыт",
        [AuditActions.PeriodReopened] = "Период открыт",
        [AuditActions.OrganizationCreated] = "Создана организация",
        [AuditActions.OrganizationRequisitesChanged] = "Изменены реквизиты",
    };

    public static string Action(string code) => Actions.TryGetValue(code, out var label) ? label : code;
}

/// <summary>Часовые пояса России для учёта (IANA).</summary>
public static class TimeZones
{
    public static readonly IReadOnlyList<(string Id, string Label)> Russian =
    [
        ("Europe/Kaliningrad", "Калининград (UTC+2)"),
        ("Europe/Moscow", "Москва (UTC+3)"),
        ("Europe/Samara", "Самара (UTC+4)"),
        ("Asia/Yekaterinburg", "Екатеринбург (UTC+5)"),
        ("Asia/Omsk", "Омск (UTC+6)"),
        ("Asia/Novosibirsk", "Новосибирск (UTC+7)"),
        ("Asia/Krasnoyarsk", "Красноярск (UTC+7)"),
        ("Asia/Irkutsk", "Иркутск (UTC+8)"),
        ("Asia/Yakutsk", "Якутск (UTC+9)"),
        ("Asia/Vladivostok", "Владивосток (UTC+10)"),
        ("Asia/Magadan", "Магадан (UTC+11)"),
        ("Asia/Kamchatka", "Камчатка (UTC+12)"),
    ];

    public static string Label(string id) => Russian.FirstOrDefault(z => z.Id == id).Label ?? id;

    public static DateTime ToLocal(DateTime utc, string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var tz)
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz)
            : utc;
}
