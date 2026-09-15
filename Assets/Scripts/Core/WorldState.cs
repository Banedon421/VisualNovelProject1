using UnityEngine;

// The single object that ties every subsystem together for one playthrough.
// Pass this (or the pieces of it you need) into anything that has to read
// or change game state -- the Ink binder, UI screens, the map, etc.
public class WorldState
{
    public PlayerProfile Player;
    public StatSystem Stats;
    public RelationshipSystem Relationships;
    public DynastyGraph Dynasty;
    public HistoryLog History;
    public MagicState Magic;
    public WorldClock Clock;

    // Kept so pole/clan ids set later (e.g. from ink) can be validated
    // against the actual definitions rather than accepted blindly.
    public GameDatabase Database;

    public WorldState(GameDatabase database, PlayerProfile player, int startYear)
    {
        Database = database;
        Player = player;
        Stats = new StatSystem(database.Stats);
        Relationships = new RelationshipSystem();
        Dynasty = new DynastyGraph();
        History = new HistoryLog();
        Magic = new MagicState();
        Clock = new WorldClock(startYear);

        ApplyClanStartingModifiers(database, player.clanId);

        // Re-validates the pole the PlayerProfile was constructed with,
        // for the same reason SetPole below validates later changes --
        // a typo in the very first assignment deserves the same warning
        // as a typo introduced later from ink.
        SetPole(player.currentPoleId);
    }

    // The only place currentPoleId should be assigned, at construction or
    // later (e.g. from InkBinder's "set_pole" external). Logs a warning
    // instead of failing silently if the id doesn't match anything in
    // poles.json -- a typo here previously would have just meant every
    // get_pole() == "..." check in ink quietly fell through to its "else"
    // branch with no indication anything was wrong.
    public void SetPole(string poleId)
    {
        if (Database?.Poles != null && !string.IsNullOrEmpty(poleId) &&
            !Database.Poles.Exists(p => p.id == poleId))
        {
            Debug.LogWarning($"WorldState: '{poleId}' does not match any pole id in poles.json -- " +
                              "setting it anyway, but every get_pole() check in ink comparing against " +
                              "it will silently fail. Check for a typo in either place.");
        }

        Player.currentPoleId = poleId;
    }

    private void ApplyClanStartingModifiers(GameDatabase database, string clanId)
    {
        var clan = database.Clans.Find(c => c.id == clanId);
        if (clan == null)
        {
            Debug.LogWarning($"WorldState: '{clanId}' does not match any clan id in clans.json -- " +
                              "no starting stat modifiers applied. Check for a typo in either place.");
            return;
        }

        if (clan.startingStatModifiers == null) return;

        foreach (var kv in clan.startingStatModifiers)
        {
            Stats.Add(kv.Key, kv.Value);
        }
    }
}
