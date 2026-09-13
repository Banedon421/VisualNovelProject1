using System.Collections.Generic;

// Tracks who has the (rare, dangerous) magic talent and how much
// accumulated cost they've paid for using it. Deliberately minimal for the
// first draft -- extend with specific cost categories (physical/social/
// moral) once the magic-system design is more settled.
public class MagicState
{
    private readonly HashSet<string> _talented = new HashSet<string>();
    private readonly Dictionary<string, float> _accumulatedCost = new Dictionary<string, float>();

    public bool HasTalent(string characterId) => _talented.Contains(characterId);
    public void GrantTalent(string characterId) => _talented.Add(characterId);

    public float GetAccumulatedCost(string characterId) =>
        _accumulatedCost.TryGetValue(characterId, out var v) ? v : 0f;

    public void AddCost(string characterId, float amount)
    {
        _accumulatedCost[characterId] = GetAccumulatedCost(characterId) + amount;
    }

    public List<string> SnapshotTalented() => new List<string>(_talented);
    public Dictionary<string, float> SnapshotCosts() => new Dictionary<string, float>(_accumulatedCost);

    public void Restore(List<string> talented, Dictionary<string, float> costs)
    {
        _talented.Clear();
        foreach (var t in talented) _talented.Add(t);
        _accumulatedCost.Clear();
        foreach (var kv in costs) _accumulatedCost[kv.Key] = kv.Value;
    }
}
