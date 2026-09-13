using System.Collections.Generic;
using System.IO;
using Ink.Runtime;
using Newtonsoft.Json;
using UnityEngine;

public static class SaveManager
{
    public static void Save(WorldState world, Story story, string filePath)
    {
        var data = new SaveData
        {
            inkStateJson = story.state.ToJson(),
            player = world.Player,
            currentYear = world.Clock.CurrentYear,
            stats = world.Stats.Snapshot(),
            relationships = world.Relationships.Snapshot(),
            dynastyMembers = world.Dynasty.Snapshot(),
            historyFlags = world.History.SnapshotFlags(),
            historyEvents = new List<HistoryEvent>(world.History.SnapshotEvents()),
            magicTalented = world.Magic.SnapshotTalented(),
            magicCosts = world.Magic.SnapshotCosts(),
        };

        File.WriteAllText(filePath, JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    // Loads state INTO an existing WorldState/Story pair rather than
    // constructing new ones, since WorldState's constructor needs the
    // GameDatabase and a fresh clan-based setup that a loaded save should
    // then override.
    public static void Load(string filePath, WorldState world, Story story)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"SaveManager: no save file at {filePath}");
            return;
        }

        var data = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(filePath));

        story.state.LoadJson(data.inkStateJson);

        world.Player.characterName = data.player.characterName;
        world.Player.clanId = data.player.clanId;
        world.Player.currentPoleId = data.player.currentPoleId;
        world.Player.birthYear = data.player.birthYear;
        world.Player.personalTraits = data.player.personalTraits;

        world.Clock.Restore(data.currentYear);
        world.Stats.Restore(data.stats);
        world.Relationships.Restore(data.relationships);
        world.Dynasty.Restore(data.dynastyMembers);
        world.History.Restore(data.historyFlags, data.historyEvents);
        world.Magic.Restore(data.magicTalented, data.magicCosts);
    }
}
