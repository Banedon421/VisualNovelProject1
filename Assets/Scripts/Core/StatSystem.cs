using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A named numeric value with bounds, e.g. "stability" 0-100.
// Definitions come from data (see DataDefinitions.cs / stats.json) so you
// can add or rename stats without touching this class.
[Serializable]
public class StatDefinition
{
    public string id;
    public string displayName;
    public float minValue;
    public float maxValue;
    public float defaultValue;
}

// Runtime container for the current value of every stat in a given
// playthrough. Deliberately generic (dictionary-based) rather than one
// hardcoded field per stat, because the exact list of stats is still
// expected to change while you iterate on the design.
public class StatSystem
{
    private readonly Dictionary<string, StatDefinition> _definitions;
    private readonly Dictionary<string, float> _values;

    public StatSystem(IEnumerable<StatDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(d => d.id);
        _values = _definitions.Values.ToDictionary(d => d.id, d => d.defaultValue);
    }

    public float Get(string statId)
    {
        if (_values.TryGetValue(statId, out var value)) return value;
        Debug.LogWarning($"StatSystem: unknown stat id '{statId}'.");
        return 0f;
    }

    public void Add(string statId, float delta) => Set(statId, Get(statId) + delta);

    public void Set(string statId, float value)
    {
        if (!_definitions.TryGetValue(statId, out var def))
        {
            Debug.LogWarning($"StatSystem: unknown stat id '{statId}', ignoring.");
            return;
        }
        _values[statId] = Mathf.Clamp(value, def.minValue, def.maxValue);
    }

    public IEnumerable<string> AllStatIds => _definitions.Keys;

    // --- Save/Load support ---
    public Dictionary<string, float> Snapshot() => new Dictionary<string, float>(_values);

    public void Restore(Dictionary<string, float> snapshot)
    {
        // Guards against a save file where this field is missing/null
        // (hand-edited, partially written, or migrated from an older
        // format) -- without this, a null here would throw on the
        // foreach below instead of just leaving stats at their defaults.
        if (snapshot == null) return;

        foreach (var kv in snapshot)
        {
            if (_values.ContainsKey(kv.Key)) _values[kv.Key] = kv.Value;
        }
    }
}
