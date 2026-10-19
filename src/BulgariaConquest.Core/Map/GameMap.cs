namespace BulgariaConquest.Core.Map;

public sealed record Province(string Id, string Iso, string Name, IReadOnlyList<string> Neighbors);

/// <summary>The board: provinces and which of them share a border.</summary>
public sealed class GameMap
{
    private readonly Dictionary<string, Province> _byId;
    private readonly Dictionary<string, HashSet<string>> _neighbors;

    public GameMap(IEnumerable<Province> provinces)
    {
        Provinces = provinces.ToList();
        _byId = Provinces.ToDictionary(p => p.Id);
        _neighbors = Provinces.ToDictionary(p => p.Id, p => p.Neighbors.ToHashSet());

        foreach (var p in Provinces)
        foreach (var n in p.Neighbors)
        {
            if (!_byId.ContainsKey(n))
                throw new InvalidDataException($"Province '{p.Id}' lists unknown neighbour '{n}'.");
            if (!_neighbors[n].Contains(p.Id))
                throw new InvalidDataException($"Neighbourship '{p.Id}' -> '{n}' is not symmetric.");
        }
    }

    public IReadOnlyList<Province> Provinces { get; }

    public Province this[string id] => _byId[id];

    public bool Contains(string id) => _byId.ContainsKey(id);

    public bool AreNeighbors(string a, string b) => _neighbors.TryGetValue(a, out var set) && set.Contains(b);

    public IReadOnlyCollection<string> NeighborsOf(string id) => _neighbors[id];
}
