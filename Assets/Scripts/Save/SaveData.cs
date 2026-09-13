using System;
using System.Collections.Generic;

// A flat, JSON-friendly snapshot of an entire playthrough: ink's own
// story position plus every piece of WorldState. This format WILL change
// as the game's data model evolves during development -- don't expect old
// save files to keep working across content changes yet. Worth adding
// versioning/migration once the design has settled down.
[Serializable]
public class SaveData
{
    public string inkStateJson;

    public PlayerProfile player;
    public int currentYear;

    public Dictionary<string, float> stats;
    public Dictionary<string, float> relationships;

    public List<FamilyMember> dynastyMembers;

    public List<string> historyFlags;
    public List<HistoryEvent> historyEvents;

    public List<string> magicTalented;
    public Dictionary<string, float> magicCosts;
}
