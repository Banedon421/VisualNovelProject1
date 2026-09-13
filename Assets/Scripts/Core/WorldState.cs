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

    public WorldState(GameDatabase database, PlayerProfile player, int startYear)
    {
        Player = player;
        Stats = new StatSystem(database.Stats);
        Relationships = new RelationshipSystem();
        Dynasty = new DynastyGraph();
        History = new HistoryLog();
        Magic = new MagicState();
        Clock = new WorldClock(startYear);

        ApplyClanStartingModifiers(database, player.clanId);
    }

    private void ApplyClanStartingModifiers(GameDatabase database, string clanId)
    {
        var clan = database.Clans.Find(c => c.id == clanId);
        if (clan?.startingStatModifiers == null) return;

        foreach (var kv in clan.startingStatModifiers)
        {
            Stats.Add(kv.Key, kv.Value);
        }
    }
}
