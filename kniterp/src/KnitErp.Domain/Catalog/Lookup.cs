using KnitErp.Domain.Common;

namespace KnitErp.Domain.Catalog;

public enum LookupKind : byte
{
    Project = 1,
    SalesChannel = 2,
}

/// <summary>
/// Простой справочник организации (D77): проекты («Подвязы — поло воротники») и каналы продаж («Сайт», «Маркетплейс»).
/// Только название; не удаляется — архивируется; у действующих одного вида название уникально.
/// </summary>
public sealed class Lookup
{
    public const int NameMaxLength = 150;

    private Lookup()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public LookupKind Kind { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Lookup Create(long organizationId, LookupKind kind, string? name) =>
        new() { OrganizationId = organizationId, Kind = kind, Name = DomainText.Require(name, NameMaxLength, "Название") };

    public void Rename(string? name)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Запись в архиве. Сначала верните её из архива.");
        }

        Name = DomainText.Require(name, NameMaxLength, "Название");
    }

    public void SetArchived(bool archived) => IsArchived = archived;

    public static string KindName(LookupKind kind) => kind switch
    {
        LookupKind.Project => "Проект",
        LookupKind.SalesChannel => "Канал продаж",
        _ => kind.ToString(),
    };
}
