using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

// Loads every design-time definition file once, at startup. All the JSON
// files this expects to find live in Assets/StreamingAssets/Data/ -- that
// specific folder name ("StreamingAssets") is required by Unity to be
// readable at runtime on every platform.
public class GameDatabase
{
    public List<ClanDefinition> Clans;
    public List<PoleDefinition> Poles;
    public List<StatDefinition> Stats;
    public List<NpcTemplateDefinition> NpcTemplates;
    public List<PositionDefinition> Positions;

    public static GameDatabase LoadFromStreamingAssets()
    {
        return new GameDatabase
        {
            Clans = LoadJson<List<ClanDefinition>>("clans.json"),
            Poles = LoadJson<List<PoleDefinition>>("poles.json"),
            Stats = LoadJson<List<StatDefinition>>("stats.json"),
            NpcTemplates = LoadJson<List<NpcTemplateDefinition>>("npc_templates.json"),
            Positions = LoadJson<List<PositionDefinition>>("positions.json"),
        };
    }

    private static T LoadJson<T>(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Data", fileName);

        // NOTE: on some platforms (notably Android/WebGL) StreamingAssets
        // can't be read with File.ReadAllText and needs UnityWebRequest
        // instead. Not a concern for a PC build, which is the first target --
        // revisit this if you add mobile/web as a platform later.
        if (!File.Exists(path))
        {
            Debug.LogError($"GameDatabase: could not find data file at {path}");
            return default;
        }

        string json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<T>(json);
    }
}
