using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using Ink.Runtime;

// Builds a visual-novel "scene" -- background, character portraits, and
// dialogue UI styling -- from JSON, then loads and runs the associated ink
// file. To change what's on screen, edit the JSON files in
// Assets/StreamingAssets/Scenes/ (or point sceneDefinitionFileName at a
// different one) -- nothing here needs touching in the Inspector for
// ordinary content changes, only when a wholly new KIND of element is
// introduced (e.g. a music cue) that this script doesn't know how to read
// yet.
public class GameBootstrapper : MonoBehaviour
{
    [Header("Scene definition")]
    [Tooltip("Filename only, read from Assets/StreamingAssets/Scenes/ at runtime.")]
    public string sceneDefinitionFileName = "village_intro.json";

    [Header("Scene content (assign once)")]
    [Tooltip("A persistent, full-screen Image already in the Canvas (anchors stretched 0,0 to 1,1), first in sibling order so it renders behind everything else.")]
    public Image backgroundImage;
    [Tooltip("An empty, full-screen RectTransform that character portraits are instantiated into. Its own position doesn't matter much since each character positions itself, but it should sit after Background and before the dialogue UI in sibling order.")]
    public RectTransform charactersContainer;
    [Tooltip("The 'Portrait' prefab -- a plain UI Image, instantiated once per character in the scene JSON.")]
    public Image characterPrefab;

    [Header("Dialogue UI (assign once)")]
    public DialogueUIController dialogueUI;

    [Header("Test player setup")]
    public string testClanId = "grue";
    public string testPoleId = "administration";
    public string testCharacterName = "Player";

    private const string ScenesFolder = "Scenes";

    void Start()
    {
        var sceneDefinition = LoadJsonFromStreamingAssets<SceneDefinition>(ScenesFolder, sceneDefinitionFileName);
        if (sceneDefinition == null) return;

        ApplyBackground(sceneDefinition.background);
        ApplyCharacters(sceneDefinition.characters);
        ApplyDialogueLayout(sceneDefinition.dialogueLayout);
        RunInk(sceneDefinition.inkFile);
    }

    private void ApplyBackground(string spriteName)
    {
        if (backgroundImage == null)
        {
            Debug.LogError("GameBootstrapper: no Background Image assigned.");
            return;
        }
        if (string.IsNullOrEmpty(spriteName)) return;

        var sprite = Resources.Load<Sprite>("Backgrounds/" + spriteName);
        if (sprite == null)
        {
            Debug.LogError($"GameBootstrapper: no background sprite found at Resources/Backgrounds/{spriteName}.png (or .jpg)");
            return;
        }
        backgroundImage.sprite = sprite;
    }

    private void ApplyCharacters(List<CharacterPlacement> characters)
    {
        if (charactersContainer == null || characterPrefab == null)
        {
            Debug.LogError("GameBootstrapper: Characters Container or Character Prefab not assigned.");
            return;
        }

        // Clear any previously-instantiated characters -- harmless on first
        // run, and means this same method can safely be called again
        // later if/when scene-switching mid-game becomes a thing.
        for (int i = charactersContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(charactersContainer.GetChild(i).gameObject);
        }

        if (characters == null) return;

        foreach (var placement in characters)
        {
            var instance = Instantiate(characterPrefab, charactersContainer);
            instance.name = "Character_" + placement.id;

            var rect = instance.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = placement.position.ToVector2();
            rect.sizeDelta = placement.size.ToVector2();

            var sprite = Resources.Load<Sprite>("Portraits/" + placement.sprite);
            if (sprite == null)
            {
                Debug.LogError($"GameBootstrapper: no portrait sprite found at Resources/Portraits/{placement.sprite}");
            }
            else
            {
                instance.sprite = sprite;
            }

            var view = instance.gameObject.AddComponent<CharacterView>();
            view.characterId = placement.id;
            view.displayName = placement.displayName;
            view.portraitImage = instance;
        }
    }

    private void ApplyDialogueLayout(string layoutFileName)
    {
        if (string.IsNullOrEmpty(layoutFileName) || dialogueUI == null) return;

        var layout = LoadJsonFromStreamingAssets<DialogueLayoutConfig>(ScenesFolder, layoutFileName);
        if (layout != null) dialogueUI.ApplyLayout(layout);
    }

    private void RunInk(string inkFileName)
    {
        if (string.IsNullOrEmpty(inkFileName))
        {
            Debug.LogError("GameBootstrapper: scene definition has no inkFile set.");
            return;
        }
        if (dialogueUI == null)
        {
            Debug.LogError("GameBootstrapper: no DialogueUIController assigned in the Inspector.");
            return;
        }

        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "Ink", inkFileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"GameBootstrapper: no compiled ink file at {path}.");
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

    private static T LoadJsonFromStreamingAssets<T>(string folder, string fileName) where T : class
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, folder, fileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"GameBootstrapper: no file found at {path}");
            return null;
        }
        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
    }
}
