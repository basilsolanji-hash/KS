namespace KnitErp.Infrastructure.Persistence;

/// <summary>
/// Объекты базы, которые не выражаются моделью EF Core. Применяются тем же шагом, что и схема.
/// Когда появятся миграции, этот SQL переедет в первую миграцию без изменений.
/// </summary>
public static class DatabaseObjects
{
    /// <summary>Журнал аудита неизменяем на уровне SQL Server: UPDATE и DELETE откатываются.</summary>
    public const string AuditImmutableTrigger = """
        CREATE OR ALTER TRIGGER [kniterp].[tr_audit_log_immutable]
        ON [kniterp].[audit_log]
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51000, N'Журнал аудита неизменяем: изменение и удаление записей запрещены.', 1;
        END
        """;
}
