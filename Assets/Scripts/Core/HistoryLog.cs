using System;
using System.Collections.Generic;

[Serializable]
public class HistoryEvent
{
    public int year;
    public string description;
}

// Two things at once, on purpose: cheap boolean flags for branching checks
// ("has_freed_slaves"), and a richer chronological log for things you might
// want to show the player later (a chronicle screen, an epilogue summary).
public class HistoryLog
{
    private readonly HashSet<string> _flags = new HashSet<string>();
    private readonly List<HistoryEvent> _events = new List<HistoryEvent>();

    public void SetFlag(string flagId) => _flags.Add(flagId);
    public bool HasFlag(string flagId) => _flags.Contains(flagId);
    public void ClearFlag(string flagId) => _flags.Remove(flagId);

    public void RecordEvent(int year, string description) =>
        _events.Add(new HistoryEvent { year = year, description = description });

    public IReadOnlyList<HistoryEvent> Events => _events;

    // --- Save/Load support ---
    public List<string> SnapshotFlags() => new List<string>(_flags);
    public List<HistoryEvent> SnapshotEvents() => new List<HistoryEvent>(_events);

    public void Restore(List<string> flags, List<HistoryEvent> events)
    {
        // See StatSystem.Restore for why these guards are here -- a
        // missing field in a save file deserializes to null, not an
        // empty list, for either parameter independently.
        _flags.Clear();
        if (flags != null)
        {
            foreach (var f in flags) _flags.Add(f);
        }

        _events.Clear();
        if (events != null)
        {
            _events.AddRange(events);
        }
    }
}
