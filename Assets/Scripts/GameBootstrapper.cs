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
//
// IMPORTANT -- Reference Resolution: any scene JSON using legacy pixel
// "position"/"size" for characters (see CharacterPlacement in
// SceneDefinition.cs) assumes a specific CanvasScaler Reference
// Resolution. Changing that value in the Inspector re-interprets those
// same pixel numbers against a different design canvas and will make
// portraits/text look wrong -- it is not a bug, it's exactly what
// "Scale With Screen Size" is supposed to do. Set Reference Resolution
// once (this project currently assumes 1920x1080) and leave it alone;
// simulate different screens by resizing the Game view or using Device
// Simulator instead. New scenes using the normalized "anchor" field are
// not sensitive to this.
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
        var database = GameDatabase.LoadFromStreamingAssets();

        var sceneDefinition = LoadJsonFromStreamingAssets<SceneDefinition>(ScenesFolder, sceneDefinitionFileName);
        if (sceneDefinition == null) return;

        ApplyBackground(sceneDefinition.background);
        ApplyCharacters(sceneDefinition.characters, database);
        ApplyDialogueLayout(sceneDefinition.dialogueLayout);

        // Single source of truth for "where are the on-screen portraits" --
        // wired here in code rather than duplicating the same RectTransform
        // reference in two separate Inspector fields, so
        // DialogueUIController can find/dim portraits by id for the
        // #speaker: highlight without you having to remember to keep two
        // Inspector slots in sync.
        if (dialogueUI != null) dialogueUI.charactersContainer = charactersContainer;

        RunInk(sceneDefinition.inkFile, database);
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

    private void ApplyCharacters(List<CharacterPlacement> characters, GameDatabase database)
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

        // charactersContainer is stretched full-screen (anchors 0,0 to
        // 1,1), so by the time we read its rect here, it already reflects
        // whatever aspect ratio Canvas Scaler resolved to on this screen --
        // that's what makes sizeNormalized below aspect-correct without any
        // extra math.
        Rect containerRect = charactersContainer.rect;

        foreach (var placement in characters)
        {
            var instance = Instantiate(characterPrefab, charactersContainer);
            instance.name = "Character_" + placement.id;

            var rect = instance.rectTransform;

            if (placement.anchor != null)
            {
                // Normalized mode: pin the portrait's pivot to a fraction
                // of the container. anchorMin == anchorMax means "point
                // anchor," not "stretch" -- sizeDelta below is then the
                // portrait's literal size, same idea as legacy mode, just
                // positioned via a fraction instead of a pixel offset from
                // center.
                rect.anchorMin = rect.anchorMax = placement.anchor.ToVector2();
                rect.anchoredPosition = Vector2.zero;

                rect.sizeDelta = placement.sizeNormalized != null
                    ? new Vector2(placement.sizeNormalized.width * containerRect.width,
                                  placement.sizeNormalized.height * containerRect.height)
                    : placement.size.ToVector2();
            }
            else
            {
                // Legacy mode: fixed pixel offset from container center, in
                // Reference Resolution units -- see the class-level comment
                // above about why Reference Resolution has to stay fixed
                // for this to keep looking right.
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = placement.position.ToVector2();
                rect.sizeDelta = placement.size.ToVector2();
            }

            // Fall back to this character's npc_templates.json entry for
            // anything the scene didn't specify itself -- keeps a recurring
            // character's name/default look from being repeated in every
            // scene file they appear in.
            var template = database.NpcTemplates?.Find(t => t.id == placement.id);

            string displayName = !string.IsNullOrEmpty(placement.displayName)
                ? placement.displayName
                : template?.baseName;

            string spriteName = !string.IsNullOrEmpty(placement.sprite)
                ? placement.sprite
                : template?.defaultSprite;

            var sprite = string.IsNullOrEmpty(spriteName) ? null : Resources.Load<Sprite>("Portraits/" + spriteName);
            if (sprite == null)
            {
                Debug.LogError($"GameBootstrapper: no portrait sprite found for character '{placement.id}' " +
                                $"(tried Resources/Portraits/{spriteName}). Check the scene JSON's \"sprite\" " +
                                "field or this character's npc_templates.json entry.");
            }
            else
            {
                instance.sprite = sprite;
            }

            var view = instance.gameObject.AddComponent<CharacterView>();
            view.characterId = placement.id;
            view.displayName = displayName;
            view.portraitImage = instance;
        }
    }

    private void ApplyDialogueLayout(string layoutFileName)
    {
        if (string.IsNullOrEmpty(layoutFileName) || dialogueUI == null) return;

        var layout = LoadJsonFromStreamingAssets<DialogueLayoutConfig>(ScenesFolder, layoutFileName);
        if (layout != null) dialogueUI.ApplyLayout(layout);
    }

    private void RunInk(string inkFileName, GameDatabase database)
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

        var player = new PlayerProfile
        {
            characterName = testCharacterName,
            clanId = testClanId,
            currentPoleId = testPoleId,
            birthYear = 1180,
        };

        var world = new WorldState(database, player, startYear: 1200);
        var story = new Story(inkJson);

        // Destroy first, in case this scene is being (re)loaded rather than
        // started fresh -- otherwise repeated calls stack a new InkBinder
        // on top of old ones without removing them. Not reachable yet
        // (Start() only runs once today), but this is exactly the kind of
        // thing that bites silently once scene-switching exists, so it's
        // handled now rather than left as a trap for later.
        var existingBinder = GetComponent<InkBinder>();
        if (existingBinder != null) Destroy(existingBinder);

        var binder = gameObject.AddComponent<InkBinder>();
        binder.World = world;
        binder.Bind(story);

        try
        {
            story.ValidateExternalBindings();
        }
        catch (System.Exception e)
        {
            Debug.LogError("GameBootstrapper: ink external function bindings are incomplete or " +
                            $"mismatched with InkBinder.cs -- {e.Message}");
            return;
        }

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
