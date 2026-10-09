using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;

namespace KnitErp.Domain.Structure;

/// <summary>
/// Подразделение фабрики (цех, участок, склад, служба). Дерево внутри организации; не удаляется — архивируется.
/// По подразделению ограничивается область данных роли «Руководитель подразделения».
/// </summary>
public sealed class Department
{
    public const int NameMaxLength = 150;

    private Department()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long? ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsArchived { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Department Create(long organizationId, string name, long? parentId, DateTime nowUtc) => new()
    {
        OrganizationId = organizationId,
        Name = StructureText.Require(name, NameMaxLength, "Название подразделения"),
        ParentId = parentId,
        CreatedAtUtc = nowUtc,
    };

    public FieldChange? Rename(string name)
    {
        EnsureActive();
        var value = StructureText.Require(name, NameMaxLength, "Название подразделения");
        if (value == Name)
        {
            return null;
        }

        var change = new FieldChange(nameof(Name), Name, value);
        Name = value;
        return change;
    }

    /// <summary>Перенос в другое подразделение. Цикл проверяет сервис по всему дереву: <paramref name="ancestorsOfNewParent"/>.</summary>
    public FieldChange? MoveTo(long? parentId, IReadOnlyCollection<long> ancestorsOfNewParent)
    {
        EnsureActive();
        if (parentId == ParentId)
        {
            return null;
        }

        if (parentId == Id || ancestorsOfNewParent.Contains(Id))
        {
            throw new BusinessRuleException("structure.department.cycle", "Нельзя перенести подразделение внутрь самого себя.");
        }

        var change = new FieldChange(nameof(ParentId), ParentId?.ToString(), parentId?.ToString());
        ParentId = parentId;
        return change;
    }

    /// <summary>В архив — только пустое: без действующих сотрудников и вложенных подразделений.</summary>
    public void Archive(int activeChildren, int activeEmployees, DateTime nowUtc)
    {
        EnsureActive();
        if (activeChildren > 0)
        {
            throw new BusinessRuleException("structure.department.has_children", "Сначала перенесите или архивируйте вложенные подразделения.");
        }

        if (activeEmployees > 0)
        {
            throw new BusinessRuleException("structure.department.has_employees",
                $"В подразделении работают сотрудники ({activeEmployees}). Сначала переведите их.");
        }

        IsArchived = true;
        ArchivedAtUtc = nowUtc;
    }

    private void EnsureActive()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("structure.department.archived", "Подразделение в архиве.");
        }
    }
}

/// <summary>Должность — справочник организации. Не удаляется — архивируется.</summary>
public sealed class Position
{
    public const int NameMaxLength = 150;

    private Position()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsArchived { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Position Create(long organizationId, string name, DateTime nowUtc) => new()
    {
        OrganizationId = organizationId,
        Name = StructureText.Require(name, NameMaxLength, "Название должности"),
        CreatedAtUtc = nowUtc,
    };

    public FieldChange? Rename(string name)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("structure.position.archived", "Должность в архиве.");
        }

        var value = StructureText.Require(name, NameMaxLength, "Название должности");
        if (value == Name)
        {
            return null;
        }

        var change = new FieldChange(nameof(Name), Name, value);
        Name = value;
        return change;
    }

    public void Archive(int activeEmployees)
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("structure.position.archived", "Должность уже в архиве.");
        }

        if (activeEmployees > 0)
        {
            throw new BusinessRuleException("structure.position.in_use",
                $"Должность занимают сотрудники ({activeEmployees}). Сначала переведите их.");
        }

        IsArchived = true;
    }
}

internal static class StructureText
{
    public static string Require(string? value, int maxLength, string field)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            throw new BusinessRuleException("structure.field.required", $"Поле «{field}» обязательно.");
        }

        if (v.Length > maxLength)
        {
            throw new BusinessRuleException("structure.field.too_long", $"Поле «{field}» длиннее {maxLength} символов.");
        }

        return v;
    }

    public static string? Optional(string? value, int maxLength, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : Require(value, maxLength, field);
}
