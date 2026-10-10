namespace KnitErp.Domain.Common;

/// <summary>
/// Счётчик номеров документов: своя сквозная нумерация у каждой организации и вида документа (НО-000001…).
/// Конкурентная выдача номера защищена rowversion: при конфликте номер берётся заново.
/// </summary>
public sealed class DocumentCounter
{
    public const int KindMaxLength = 20;

    private DocumentCounter()
    {
    }

    public long OrganizationId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public long LastNumber { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static DocumentCounter Start(long organizationId, string kind) => new() { OrganizationId = organizationId, Kind = kind };

    public string Next()
    {
        LastNumber++;
        return $"{Kind}-{LastNumber:D6}";
    }
}
