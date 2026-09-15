using System.Collections.Generic;

// A single generic ledger of "how does the world feel about you" (and vice
// versa), keyed by an arbitrary string id. The same system is used for NPCs,
// clans, and poles alike -- mechanically they're all just a number that goes
// up or down and gets checked in conditions. Use a naming convention for the
// id if you want to filter/list them by category later, e.g.
// "npc:dupont", "clan:crabe", "pole:army".
//
// NOTE: this is deliberately a single scalar per target. If you later want
// multiple axes (e.g. trust vs. fear) for some relationships, that's a
// bigger change -- flag it before leaning on this for romance/family
// mechanics specifically, since those may need more than one number.
public class RelationshipSystem
{
    private readonly Dictionary<string, float> _values = new Dictionary<string, float>();

    public float Get(string targetId) => _values.TryGetValue(targetId, out var v) ? v : 0f;

    public void Add(string targetId, float delta) => Set(targetId, Get(targetId) + delta);

    public void Set(string targetId, float value) => _values[targetId] = value;

    public Dictionary<string, float> Snapshot() => new Dictionary<string, float>(_values);

    public void Restore(Dictionary<string, float> snapshot)
    {
        // See StatSystem.Restore for why this guard is here -- a missing
        // field in a save file deserializes to null, not an empty dict.
        if (snapshot == null) return;

        _values.Clear();
        foreach (var kv in snapshot) _values[kv.Key] = kv.Value;
    }
}
