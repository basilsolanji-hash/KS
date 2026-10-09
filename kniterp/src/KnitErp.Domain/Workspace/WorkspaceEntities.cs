using KnitErp.Domain.Common;

namespace KnitErp.Domain.Workspace;

/// <summary>
/// Личные данные инструментов пользователя в организации: настройка панели быстрого доступа, задачи и календарь,
/// заметки, ссылки, избранное, город погоды. Хранятся на сервере — доступны с любого устройства.
/// Одна запись на вид данных; содержимое — JSON, который проверяет сервис.
/// </summary>
public sealed class UserToolData
{
    public const int KindMaxLength = 40;

    /// <summary>Ограничение размера одного вида данных (~200 тысяч символов): личные инструменты — не хранилище файлов.</summary>
    public const int MaxJsonLength = 200_000;

    private UserToolData()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long UserId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string Json { get; private set; } = "{}";
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static UserToolData Create(long organizationId, long userId, string kind, string json, DateTime nowUtc)
    {
        var data = new UserToolData { OrganizationId = organizationId, UserId = userId, Kind = kind };
        data.Update(json, nowUtc);
        return data;
    }

    public void Update(string json, DateTime nowUtc)
    {
        if (json.Length > MaxJsonLength)
        {
            throw new BusinessRuleException("workspace.too_large", "Слишком много данных — удалите старые записи.");
        }

        Json = json;
        UpdatedAtUtc = nowUtc;
    }
}

public enum SupportTicketStatus : byte
{
    Open = 1,
    InProgress = 2,
    Answered = 3,
    Closed = 9,
}

/// <summary>
/// Обращение в поддержку: пользователь описывает проблему, администратор организации отвечает и ведёт статус
/// (допущение D48 — поддержка внутри организации; внешний сервис поддержки подключается позже).
/// </summary>
public sealed class SupportTicket
{
    public const string NumberPrefix = "ОБ";
    public const int SubjectMaxLength = 200;
    public const int TextMaxLength = 4000;
    public const int SectionMaxLength = 200;

    private SupportTicket()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public long AuthorUserId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;

    /// <summary>Раздел системы, откуда создано обращение, — помогает понять контекст.</summary>
    public string? Section { get; private set; }

    public SupportTicketStatus Status { get; private set; }
    public string? Answer { get; private set; }
    public long? AnsweredByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Автор ещё не видел последнее изменение статуса — показывается в уведомлениях.</summary>
    public bool UnreadByAuthor { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static SupportTicket Create(
        long organizationId, string number, long authorUserId, string? subject, string? text, string? section, DateTime nowUtc) => new()
    {
        OrganizationId = organizationId,
        Number = number,
        AuthorUserId = authorUserId,
        Subject = DomainText.Require(subject, SubjectMaxLength, "Тема"),
        Text = DomainText.Require(text, TextMaxLength, "Описание"),
        Section = DomainText.Optional(section, SectionMaxLength, "Раздел"),
        Status = SupportTicketStatus.Open,
        CreatedAtUtc = nowUtc,
        UpdatedAtUtc = nowUtc,
    };

    public void TakeInWork(DateTime nowUtc)
    {
        EnsureNotClosed();
        Status = SupportTicketStatus.InProgress;
        UpdatedAtUtc = nowUtc;
        UnreadByAuthor = true;
    }

    public void Reply(string? answer, long userId, DateTime nowUtc)
    {
        EnsureNotClosed();
        Answer = DomainText.Require(answer, TextMaxLength, "Ответ");
        AnsweredByUserId = userId;
        Status = SupportTicketStatus.Answered;
        UpdatedAtUtc = nowUtc;
        UnreadByAuthor = true;
    }

    public void Close(DateTime nowUtc)
    {
        EnsureNotClosed();
        Status = SupportTicketStatus.Closed;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkReadByAuthor() => UnreadByAuthor = false;

    public static string StatusName(SupportTicketStatus status) => status switch
    {
        SupportTicketStatus.Open => "Новое",
        SupportTicketStatus.InProgress => "В работе",
        SupportTicketStatus.Answered => "Есть ответ",
        SupportTicketStatus.Closed => "Закрыто",
        _ => status.ToString(),
    };

    private void EnsureNotClosed()
    {
        if (Status == SupportTicketStatus.Closed)
        {
            throw new BusinessRuleException("support.closed", "Обращение закрыто.");
        }
    }
}
