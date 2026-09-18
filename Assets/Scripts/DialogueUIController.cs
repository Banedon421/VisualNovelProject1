using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ink.Runtime;

// Drives the dialogue UI from an ink Story. ApplyLayout lets a
// DialogueLayoutConfig (loaded from JSON by GameBootstrapper) control this
// UI's position, size, colors, and 9-slice border sprites -- unchanged from
// before.
//
// UPDATED: text now advances through a coroutine (RefreshRoutine) instead
// of a single synchronous loop, which is what makes the following possible:
//
//   - Typewriter reveal, speed controlled by "charsPerSecond" (adjustable
//     at runtime, or per-line from ink via a "# speed:<n>" tag).
//   - Click-anywhere-except-a-button fast-forwards whatever's currently
//     happening (typing a line, waiting out a pause, waiting on a page
//     break) -- see Update()/IsPointerOverButton().
//   - Per-line pacing against StageDirector animations, via new ink tags
//     alongside the existing "# speaker:id" convention:
//       # wait:all        -- wait until every on-screen character is idle
//       # wait:<id>        -- wait until just that character is idle
//       # pause:<ms>       -- fixed pause, independent of animations
//     "waitForAnimationsByDefault" (on by default) makes this automatic
//     even with NO tag present: after any line, if something is still
//     animating, the next line waits for it. Tags are only needed for
//     finer control (waiting on one character specifically, or a pause
//     unrelated to any animation) -- most content needs no tags at all.
//   - Automatic pagination: before typing a line, checks whether it would
//     overflow the text area (TextMeshPro's built-in Page overflow mode
//     does the measuring) and if so, pauses, waits for a click, clears the
//     box, and continues -- no ink-side markup needed for this at all.
//
// Speaker dimming is now naturally per-LINE instead of per merged text
// block, since lines are processed one at a time anyway -- this quietly
// resolves the old "only the last line's tag wins" limitation.
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

    [Header("Typewriter / pacing (new)")]
    [Tooltip("Wired automatically by GameBootstrapper.Start(), same as charactersContainer -- needed so this controller can ask StageDirector 'is anyone still animating' for pause tags and the default-wait behavior below. Leave empty to disable animation-aware waits entirely (pause tags/defaults become no-ops).")]
    public StageDirector stage;
    [Tooltip("Text reveal speed in characters per second. Adjustable at runtime (e.g. from a settings menu) or per-line from ink via a '# speed:<n>' tag.")]
    public float charsPerSecond = 40f;
    [Tooltip("If true (default), a line with no explicit '# wait:'/'# pause:' tag automatically waits for every on-screen character to finish animating before the next line starts. If false, pacing is 'fire and forget' as before, and only lines with an explicit tag will wait.")]
    public bool waitForAnimationsByDefault = true;

    private Story _story;
    private readonly List<Button> _spawnedButtons = new List<Button>();
    private string _currentSpeakerId;
    private Coroutine _activeRoutine;
    private bool _skipRequested;

    // Text already committed to the current page (joined by "\n"),
    // i.e. what would still be on screen if we froze right now. Reset to
    // "" whenever a page break happens.
    private string _pageAccumulated = "";

    // Cached from the last ApplyLayout call, applied to every choice
    // button spawned afterward in SpawnChoiceButtons().
    private Sprite _choiceBorderSprite;
    private Color _choiceColor = Color.white;

    private enum PauseKind { None, WaitAll, WaitCharacter, Duration }

    void Awake()
    {
        if (storyText != null)
        {
            // Page mode is what makes automatic pagination possible below --
            // TMP will tell us via textInfo.pageCount whether a given block
            // of text fits in one page (i.e. in this RectTransform) or not,
            // without us having to measure anything by hand.
            storyText.overflowMode = TextOverflowModes.Page;
            storyText.pageToDisplay = 1;
        }
    }

    void Update()
    {
        // Any left-click that does NOT land on a Button counts as "the
        // player wants to move things along" -- speeds up the current
        // line's typing, skips a pause/wait, or advances a page break.
        // Clicks that DO land on a Button are left alone entirely so a
        // choice click only ever does the one thing it's supposed to.
        if (Input.GetMouseButtonDown(0) && !IsPointerOverButton())
        {
            _skipRequested = true;
        }
    }

    private bool IsPointerOverButton()
    {
        if (EventSystem.current == null) return false;
        var pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        foreach (var result in results)
        {
            if (result.gameObject.GetComponentInParent<Button>() != null) return true;
        }
        return false;
    }

    public void Bind(Story story)
    {
        _story = story;
        StartRefresh();
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

    // --- Main line-by-line flow ---

    private void StartRefresh()
    {
        if (_activeRoutine != null) StopCoroutine(_activeRoutine);
        _activeRoutine = StartCoroutine(RefreshRoutine());
    }

    private IEnumerator RefreshRoutine()
    {
        ClearChoiceButtons();
        _pageAccumulated = "";
        storyText.text = "";
        _skipRequested = false;

        while (_story.canContinue)
        {
            string line = _story.Continue().Trim();

            ParseLineTags(_story.currentTags, out var pauseKind, out var waitTargetId, out var pauseSeconds);
            UpdateSpeakerHighlight();

            yield return RevealLine(line);
            yield return HandlePause(pauseKind, waitTargetId, pauseSeconds);
        }

        if (_story.currentChoices.Count == 0)
        {
            storyText.text = _pageAccumulated + "\n\n--- END ---";
        }
        else
        {
            SpawnChoiceButtons();
        }

        _activeRoutine = null;
    }

    // Parses this line's tags. Same "last one wins if seen more than once"
    // approach the original #speaker: parsing used, extended with three
    // new prefixes. Unrecognized tags are ignored, same as before.
    private void ParseLineTags(List<string> tags, out PauseKind pauseKind, out string waitTargetId, out float pauseSeconds)
    {
        pauseKind = PauseKind.None;
        waitTargetId = null;
        pauseSeconds = 0f;

        if (tags == null) return;

        foreach (var tag in tags)
        {
            if (tag.StartsWith("speaker:"))
            {
                _currentSpeakerId = tag.Substring("speaker:".Length).Trim();
            }
            else if (tag.StartsWith("wait:"))
            {
                string target = tag.Substring("wait:".Length).Trim();
                if (target == "all") { pauseKind = PauseKind.WaitAll; }
                else { pauseKind = PauseKind.WaitCharacter; waitTargetId = target; }
            }
            else if (tag.StartsWith("pause:"))
            {
                if (float.TryParse(tag.Substring("pause:".Length).Trim(), out var ms))
                {
                    pauseKind = PauseKind.Duration;
                    pauseSeconds = ms / 1000f;
                }
            }
            else if (tag.StartsWith("speed:"))
            {
                if (float.TryParse(tag.Substring("speed:".Length).Trim(), out var cps))
                {
                    charsPerSecond = cps;
                }
            }
        }
    }

    // Checks whether this line would overflow the current page BEFORE
    // typing it (using TMP's Page overflow mode to measure), and if so,
    // pauses for a page-break click first. Page breaks only ever happen
    // between lines, never mid-line -- splitting one sentence across a
    // page read badly anyway, and it keeps this logic simple.
    private IEnumerator RevealLine(string line)
    {
        string candidate = _pageAccumulated.Length > 0 ? _pageAccumulated + "\n" + line : line;
        storyText.text = candidate;
        storyText.ForceMeshUpdate();
        bool overflows = storyText.textInfo.pageCount > 1;

        // Put the box back to "not yet typed" either way -- the check
        // above needed the full candidate text to measure accurately, but
        // we don't want it to have flashed at full length even for a frame.
        storyText.text = _pageAccumulated;

        if (overflows)
        {
            yield return WaitForAdvanceClick();
            _pageAccumulated = "";
        }

        yield return TypeLine(line);
    }

    private IEnumerator WaitForAdvanceClick()
    {
        _skipRequested = false;
        while (!_skipRequested) yield return null;
        _skipRequested = false;
        storyText.text = "";
    }

    private IEnumerator TypeLine(string line)
    {
        string prefix = _pageAccumulated.Length > 0 ? _pageAccumulated + "\n" : "";
        int shown = 0;
        while (shown < line.Length)
        {
            shown = _skipRequested ? line.Length : shown + 1;
            storyText.text = prefix + line.Substring(0, shown);
            if (shown < line.Length) yield return new WaitForSeconds(1f / Mathf.Max(1f, charsPerSecond));
        }
        _skipRequested = false;
        _pageAccumulated = prefix + line;
    }

    private IEnumerator HandlePause(PauseKind kind, string waitTargetId, float pauseSeconds)
    {
        _skipRequested = false;
        switch (kind)
        {
            case PauseKind.WaitAll:
                yield return WaitForAnimations(null);
                break;
            case PauseKind.WaitCharacter:
                yield return WaitForAnimations(waitTargetId);
                break;
            case PauseKind.Duration:
                float t = 0f;
                while (t < pauseSeconds && !_skipRequested) { t += Time.deltaTime; yield return null; }
                break;
            case PauseKind.None:
            default:
                if (waitForAnimationsByDefault) yield return WaitForAnimations(null);
                break;
        }
        _skipRequested = false;
    }

    private IEnumerator WaitForAnimations(string onlyCharacterId)
    {
        if (stage == null) yield break;
        while (!_skipRequested &&
               (onlyCharacterId != null ? stage.IsAnimating(onlyCharacterId) : stage.IsAnyAnimating()))
        {
            yield return null;
        }
    }

    // --- Choices ---

    private void SpawnChoiceButtons()
    {
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

    private void SelectChoice(int index)
    {
        _story.ChooseChoiceIndex(index);
        StartRefresh();
    }

    private void ClearChoiceButtons()
    {
        // Prevent a reference to a soon-destroyed button from lingering as
        // "selected" in the EventSystem and bleeding a stuck highlighted
        // state onto whatever takes its place in the hierarchy -- this is
        // what caused all choice buttons to appear highlighted after a
        // click (Destroy() is deferred to end-of-frame, so the old and new
        // buttons briefly coexist while EventSystem still points at one
        // that's about to disappear).
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        foreach (var button in _spawnedButtons)
        {
            Destroy(button.gameObject);
        }
        _spawnedButtons.Clear();
    }

    // --- Speaker dimming ---

    private void UpdateSpeakerHighlight()
    {
        if (charactersContainer == null) return;

        var views = charactersContainer.GetComponentsInChildren<CharacterView>();

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
}
