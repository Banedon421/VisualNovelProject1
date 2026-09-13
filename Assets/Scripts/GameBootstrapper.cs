using System.IO;
using UnityEngine;
using Ink.Runtime;

// Loads the compiled ink story from StreamingAssets (kept in sync
// automatically by InkStreamingAssetsExporter.cs whenever you save
// main.ink), builds the WorldState, wires the Ink <-> C# bindings, and
// hands the running Story off to the UI controller. This file no longer
// needs an "Ink Asset" field at all -- that whole Inspector drag-and-drop
// step is gone, and this same code path works identically in the Editor
// and in an exported build.
public class GameBootstrapper : MonoBehaviour
{
    [Header("Ink")]
    [Tooltip("Filename only, e.g. 'main.json'. Read from Assets/StreamingAssets/Ink/ at runtime.")]
    public string inkStoryFileName = "main.json";

    [Header("UI")]
    public DialogueUIController dialogueUI;

    [Header("Test player setup")]
    public string testClanId = "grue";
    public string testPoleId = "administration";
    public string testCharacterName = "Player";

    void Start()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "Ink", inkStoryFileName);

        if (!File.Exists(path))
        {
            Debug.LogError($"GameBootstrapper: no compiled ink file at {path}. " +
                            "In the Editor this is generated automatically when main.ink is saved " +
                            "(look for an 'Ink export: wrote ...' line in the Console). If it's " +
                            "missing, run Tools > Ink > Export All Ink Files to StreamingAssets once.");
            return;
        }

        if (dialogueUI == null)
        {
            Debug.LogError("GameBootstrapper: no DialogueUIController assigned in the Inspector.");
            return;
        }

        string inkJson = File.ReadAllText(path);

        var database = GameDatabase.LoadFromStreamingAssets();

        var player = new PlayerProfile
        {
            characterName = testCharacterName,
            clanId = testClanId,
            currentPoleId = testPoleId,
            birthYear = 1180,
        };

        var world = new WorldState(database, player, startYear: 1200);
        var story = new Story(inkJson);

        var binder = gameObject.AddComponent<InkBinder>();
        binder.World = world;
        binder.Bind(story);

        dialogueUI.Bind(story);
    }
}
