namespace KnitErp.Domain.Structure;

/// <summary>
/// Дерево подразделений в памяти: поддерево для области данных и предки для проверки переноса.
/// Подразделений на фабрике десятки, поэтому дерево читается целиком.
/// </summary>
public sealed class DepartmentTree
{
    private readonly Dictionary<long, long?> _parents;
    private readonly ILookup<long, long> _children;

    public DepartmentTree(IEnumerable<(long Id, long? ParentId)> nodes)
    {
        _parents = nodes.ToDictionary(n => n.Id, n => n.ParentId);
        _children = _parents.Where(p => p.Value is not null).ToLookup(p => p.Value!.Value, p => p.Key);
    }

    public bool Contains(long id) => _parents.ContainsKey(id);

    /// <summary>
    /// Подразделения вместе со всеми вложенными. Допущение D29: область «своё подразделение» включает вложенные участки.
    /// </summary>
    public HashSet<long> WithDescendants(IEnumerable<long> roots)
    {
        var result = new HashSet<long>();
        var queue = new Queue<long>(roots.Where(Contains));
        while (queue.TryDequeue(out var id))
        {
            if (!result.Add(id))
            {
                continue;
            }

            foreach (var child in _children[id])
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>Предки подразделения (без него самого), от родителя к корню.</summary>
    public List<long> AncestorsOf(long id)
    {
        var result = new List<long>();
        var seen = new HashSet<long> { id };
        var current = _parents.GetValueOrDefault(id);
        while (current is { } parent && seen.Add(parent))
        {
            result.Add(parent);
            current = _parents.GetValueOrDefault(parent);
        }

        return result;
    }
}
