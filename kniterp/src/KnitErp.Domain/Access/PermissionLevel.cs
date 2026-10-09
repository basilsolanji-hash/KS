namespace KnitErp.Domain.Access;

/// <summary>Значения ячеек матрицы P0 (ТЗ §4.8).</summary>
public enum PermissionLevel : byte
{
    /// <summary>«—»: нет права.</summary>
    None = 0,

    /// <summary>«Ч»: только чтение.</summary>
    ReadOnly = 1,

    /// <summary>«О»: только своя область (подразделения, склады).</summary>
    Scoped = 2,

    /// <summary>«Д»: да, во всей организации.</summary>
    Full = 3,

    /// <summary>«П»: по отдельному праву. Роль сама его не даёт, нужна индивидуальная выдача.</summary>
    ByGrant = 10,

    /// <summary>«С»: только собственные данные пользователя.</summary>
    OwnOnly = 11,
}

public static class PermissionLevelExtensions
{
    /// <summary>Даёт ли уровень доступ к данным организации (без собственных и «по отдельному праву»).</summary>
    public static bool Grants(this PermissionLevel level) =>
        level is PermissionLevel.Full or PermissionLevel.Scoped or PermissionLevel.ReadOnly;

    public static string Symbol(this PermissionLevel level) => level switch
    {
        PermissionLevel.Full => "Д",
        PermissionLevel.Scoped => "О",
        PermissionLevel.ReadOnly => "Ч",
        PermissionLevel.ByGrant => "П",
        PermissionLevel.OwnOnly => "С",
        _ => "—",
    };
}
