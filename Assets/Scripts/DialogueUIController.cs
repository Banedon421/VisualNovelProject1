using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ink.Runtime;

// Drives the dialogue UI from an ink Story: one text block for the
// accumulated story text, and a dynamically-built list of choice buttons.
// ApplyLayout additionally lets a DialogueLayoutConfig (loaded from JSON by
// GameBootstrapper) control this UI's position, size, colors, and now the
// 9-slice border sprites for the panel and choice pills -- so none of that
// needs re-wiring by hand in the Inspector when it changes.
//
// Also gives CharacterView its first real payoff: whenever the most
// recently continued ink line carries a "# speaker:id" tag, every other
// on-screen portrait gets dimmed. Deliberately simple -- whole-portrait
// alpha only, no per-line granularity (see UpdateSpeakerHighlight for why),
// no mood/position change yet -- as a cheap first step toward the
// mood-swap/positioning features CharacterView was built ahead of.
public class DialogueUIController : MonoBehaviour
{
    [Header("UI references (assign in Inspector)")]
    public TMP_Text storyText;
    public RectTransform choiceContainer;
    public Button choiceButtonPrefab;

    [Header("Layout target (assign once)")]
    [Tooltip("The semi-transparent box behind the story text. Its RectTransform IS the text area -- storyText should be a child of this object, stretched to fill it with some padding, so it moves and resizes along with the box automatically.")]
    public Image dialoguePanelBackground;

    [Header("Speaker highlight (optional)")]
    [Tooltip("Wired automatically by GameBootstrapper.Start() -- the same container it instantiates character portraits into. Leave empty (or let GameBootstrapper assign it) to disable speaker dimming entirely.")]
    public RectTransform charactersContainer;
    [Tooltip("Portrait opacity for every character NOT currently speaking, once a recognized #speaker: tag has been seen. 1 = no dimming.")]
    [Range(0f, 1f)] public float nonSpeakerAlpha = 0.55f;

    private Story _story;
    private readonly List<Button> _spawnedButtons = new List<Button>();
    private string _currentSpeakerId;

    // Cached from the last ApplyLayout call, applied to every choice
    // button spawned afterward in Refresh().
    private Sprite _choiceBorderSprite;
    private Color _choiceColor = Color.white;

    public void Bind(Story story)
    {
        _story = story;
        Refresh();
    }

    public void ApplyLayout(DialogueLayoutConfig config)
    {
        if (config == null) return;

        if (dialoguePanelBackground != null)
        {
            if (ColorUtility.TryParseHtmlString(config.boxColor, out var boxColor))
            {
                dialoguePanelBackground.color = boxColor;
            }

            ApplyBorderSprite(dialoguePanelBackground, config.panelBorderSprite, "panel");

            var panelRect = dialoguePanelBackground.rectTransform;
            panelRect.anchorMin = config.textAreaAnchorMin.ToVector2();
            panelRect.anchorMax = config.textAreaAnchorMax.ToVector2();
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
        }

        if (storyText != null)
        {
            if (ColorUtility.TryParseHtmlString(config.textColor, out var textColor))
            {
                storyText.color = textColor;
            }
            storyText.fontSize = config.fontSize;
        }

        if (choiceContainer != null)
        {
            choiceContainer.anchorMin = config.choiceAreaAnchorMin.ToVector2();
            choiceContainer.anchorMax = config.choiceAreaAnchorMax.ToVector2();
            choiceContainer.offsetMin = Vector2.zero;
            choiceContainer.offsetMax = Vector2.zero;
        }

        // Cache choice styling for Refresh() -- buttons don't exist yet at
        // ApplyLayout time (Bind/Refresh runs after this), so there's
        // nothing to apply this TO until the first choice list is built.
        _choiceBorderSprite = string.IsNullOrEmpty(config.choiceBorderSprite)
            ? null
            : Resources.Load<Sprite>("UI/" + config.choiceBorderSprite);
        if (!string.IsNullOrEmpty(config.choiceBorderSprite) && _choiceBorderSprite == null)
        {
            Debug.LogWarning($"DialogueUIController: no sprite found at Resources/UI/{config.choiceBorderSprite} " +
                              "-- choices will use the Button prefab's own default look.");
        }

        if (!ColorUtility.TryParseHtmlString(config.choiceColor, out _choiceColor))
        {
            Debug.LogWarning($"DialogueUIController: couldn't parse choiceColor '{config.choiceColor}', keeping previous value.");
        }
    }

    private void ApplyBorderSprite(Image target, string spriteName, string label)
    {
        if (string.IsNullOrEmpty(spriteName)) return;

        var sprite = Resources.Load<Sprite>("UI/" + spriteName);
        if (sprite == null)
        {
            Debug.LogWarning($"DialogueUIController: no sprite found at Resources/UI/{spriteName} -- {label} stays a flat color.");
            return;
        }
        target.sprite = sprite;
        target.type = Image.Type.Sliced;
    }

    private void Refresh()
    {
        ClearChoiceButtons();

        var sb = new StringBuilder();
        while (_story.canContinue)
        {
            string line = _story.Continue().Trim();
            sb.AppendLine(line);

            // Remember the speaker of the most recently continued line, if
            // tagged -- see UpdateSpeakerHighlight below. Every line up to
            // the next choice/pause gets merged into one text block, so
            // whichever line is LAST wins as "the current speaker" for
            // this block. Good enough for dimming the non-speaker; true
            // per-line highlighting would need rendering one block per
            // Continue() call instead of one merged blob, which is a
            // bigger structural change than this pass is aiming for.
            foreach (var tag in _story.currentTags)
            {
                if (tag.StartsWith("speaker:"))
                    _currentSpeakerId = tag.Substring("speaker:".Length).Trim();
            }
        }
        storyText.text = sb.ToString();

        UpdateSpeakerHighlight();

        for (int i = 0; i < _story.currentChoices.Count; i++)
        {
            int choiceIndex = i; // local copy -- otherwise every button's
                                  // listener would capture the same 'i'
            Choice choice = _story.currentChoices[i];

            Button button = Instantiate(choiceButtonPrefab, choiceContainer);
            button.GetComponentInChildren<TMP_Text>().text = choice.text;
            button.onClick.AddListener(() => SelectChoice(choiceIndex));
            ApplyChoiceStyle(button);
            _spawnedButtons.Add(button);
        }

        if (_story.currentChoices.Count == 0)
        {
            storyText.text += "\n\n--- END ---";
        }
    }

    private void ApplyChoiceStyle(Button button)
    {
        var img = button.GetComponent<Image>();
        if (img == null) return;

        if (_choiceBorderSprite != null)
        {
            img.sprite = _choiceBorderSprite;
            img.type = Image.Type.Sliced;
        }

        // Set the ColorBlock itself, NOT img.color directly -- Selectable
        // overwrites its target Graphic's color on every state transition
        // (pointer enter/exit, press), so a one-off img.color assignment
        // here would get silently reset back to the prefab's default
        // white the moment the player's mouse leaves the button. Building
        // normal/highlighted/pressed from the configured choiceColor keeps
        // every interaction state on-theme instead of just the resting one.
        var colors = button.colors;
        colors.normalColor = _choiceColor;
        colors.highlightedColor = Brighten(_choiceColor, 1.18f);
        colors.pressedColor = Brighten(_choiceColor, 0.82f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private static Color Brighten(Color c, float factor)
    {
        return new Color(Mathf.Clamp01(c.r * factor), Mathf.Clamp01(c.g * factor), Mathf.Clamp01(c.b * factor), c.a);
    }

    private void UpdateSpeakerHighlight()
    {
        if (charactersContainer == null) return;

        var views = charactersContainer.GetComponentsInChildren<CharacterView>();

        // If the tagged speaker isn't actually one of the portraits on
        // screen right now (an off-screen narrator line, a typo'd id, or
        // no tag seen yet this scene), don't dim anyone -- guessing wrong
        // here is worse than doing nothing, and would otherwise leave
        // whoever was dimmed by the LAST recognized speaker stuck dim.
        bool speakerOnScreen = false;
        if (!string.IsNullOrEmpty(_currentSpeakerId))
        {
            foreach (var v in views)
            {
                if (v.characterId == _currentSpeakerId) { speakerOnScreen = true; break; }
            }
        }

        foreach (var view in views)
        {
            if (view.portraitImage == null) continue;
            var c = view.portraitImage.color;
            c.a = (!speakerOnScreen || view.characterId == _currentSpeakerId) ? 1f : nonSpeakerAlpha;
            view.portraitImage.color = c;
        }
    }

    private void SelectChoice(int index)
    {
        _story.ChooseChoiceIndex(index);
        Refresh();
    }

    private void ClearChoiceButtons()
    {
        foreach (var button in _spawnedButtons)
        {
            Destroy(button.gameObject);
        }
        _spawnedButtons.Clear();
    }
}
