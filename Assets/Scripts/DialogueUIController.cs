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
//     at runtime, or mid-line from ink via "<speed:n>").
//   - Click-anywhere-except-a-button fast-forwards whatever's currently
//     happening (typing a line, waiting out a pause, waiting on a page
//     break) -- see Update()/IsPointerOverButton().
//   - Inline commands, written directly in the ink story text (NOT ink's
//     own "# tag" mechanism -- ink tags run to the end of the line, so
//     there's no way to have visible text continue after one on the same
//     line). A command is "<name>" or "<name:arg>", anywhere in the text,
//     parsed out by ParseLineParts before typing begins:
//       <speaker:id>   -- who's currently speaking, for portrait dimming
//       <speed:n>      -- typing speed from this point on, in chars/sec
//       <wait>         -- shorthand for <wait:click>
//       <wait:click>   -- pause for a click; text after it continues on
//                         the SAME line/page, no clear
//       <wait:all>     -- pause until every on-screen character is idle
//       <wait:id>      -- pause until just that character is idle
//       <pause:ms>     -- fixed pause, independent of animations
//       <page>         -- force a page turn right here: wait for a click,
//                         clear the box, keep going -- works mid-line too,
//                         not just at the end of one
//     "waitForAnimationsByDefault" (on by default) is separate from all of
//     this: it automatically waits for every on-screen character to finish
//     animating after each full ink line, even with no <wait> anywhere in
//     it -- the inline commands above are only needed for finer control
//     (a specific character, a plain pause, a manual page break).
//   - Automatic pagination, word-level by default: each stretch of plain
//     text (between/around any inline commands) is checked against the
//     page ONCE per stretch, not per character; if it doesn't fit, a
//     fresh page is tried; if it still doesn't fit even alone, it's split
//     at word boundaries (never mid-word), and only a single word too
//     long to fit a blank page on its own ever gets split by character.
//     See RevealSegment's own comment for the exact rule order.
//
// Speaker dimming is now naturally per-<speaker:id> command rather than
// per whole merged text block, since it can appear (and take effect)
// anywhere within a line's text, not just attached to the line as a whole.
//
// Four more additions, all optional (each does nothing if left
// unconfigured):
//   - "<name:id>" -- inline command resolved at PARSE time (not a paced
//     command) into that character's display name, wrapped in their
//     configured color from npc_templates.json's new "nameColor" field.
//     See ResolveNameMarkup.
//   - Typewriter blip sound (typewriterAudioSource/typewriterBlipClips) --
//     a random short clip per eligible revealed character, skipped for
//     whitespace and thinned by "blipEveryNChars" so it doesn't become a
//     wall of noise. See PlayTypewriterBlip, called from TypeCharacters.
//   - Auto-advance (autoAdvance/autoAdvanceDelay) -- page breaks and
//     <wait:click> pauses resolve on their own after a delay instead of
//     requiring a click; a click still works too and cuts the wait short.
//     Hooks in once, at WaitForClick, since every page break is already
//     built on top of it. Never auto-picks a choice.
//   - Backlog (historyToggleKey/historyPanel) -- a capped list of the
//     most recent lines (see the "Backlog / history" region below),
//     shown in a panel that's either assigned by hand or built
//     automatically at runtime the first time it's opened.
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
    [Tooltip("Portrait opacity for every character NOT currently speaking, once a '<speaker:id>' command has been seen in the text. 1 = no dimming.")]
    [Range(0f, 1f)] public float nonSpeakerAlpha = 0.55f;

    [Header("Typewriter / pacing (new)")]
    [Tooltip("Wired automatically by GameBootstrapper.Start(), same as charactersContainer -- needed so this controller can ask StageDirector 'is anyone still animating' for '<wait:...>' commands and the default-wait behavior below. Leave empty to disable animation-aware waits entirely (they become no-ops).")]
    public StageDirector stage;
    [Tooltip("Text reveal speed in characters per second. Adjustable at runtime (e.g. from a settings menu) or mid-line from ink via '<speed:n>'.")]
    public float charsPerSecond = 40f;
    [Tooltip("If true (default), every full ink line automatically waits for every on-screen character to finish animating before the next line starts, even with no '<wait:...>' anywhere in it. If false, pacing is 'fire and forget' unless the text explicitly includes a <wait:...> command.")]
    public bool waitForAnimationsByDefault = true;

    [Header("Name coloring (optional)")]
    [Tooltip("Wired automatically by GameBootstrapper.Start() -- needed so '<name:id>' can look up a character's display name and color from npc_templates.json. Leave empty to disable '<name:id>' (it will just show the raw id).")]
    public GameDatabase database;

    [Header("Typewriter blip sound (optional)")]
    [Tooltip("Leave empty to disable typing sound entirely.")]
    public AudioSource typewriterAudioSource;
    [Tooltip("One or more short blip clips -- a random one plays per eligible character, for a bit of variation. Leave empty to disable.")]
    public AudioClip[] typewriterBlipClips;
    [Tooltip("Only every Nth revealed (non-whitespace) character plays a blip, so typing doesn't turn into a wall of noise.")]
    [Range(1, 6)] public int blipEveryNChars = 2;
    [Tooltip("Random pitch range applied per blip, for a bit of chatter-like variation.")]
    public Vector2 blipPitchRange = new Vector2(0.95f, 1.05f);

    [Header("Auto-advance (optional)")]
    [Tooltip("If true, page breaks and <wait:click> pauses automatically continue after autoAdvanceDelay seconds instead of requiring a click (a click still works too, and immediately triggers the advance). Does NOT auto-pick choices -- those always wait for the player.")]
    public bool autoAdvance = false;
    [Tooltip("Seconds to wait before auto-advancing past a page break or <wait:click>, when autoAdvance is enabled.")]
    public float autoAdvanceDelay = 1.5f;

    [Header("Backlog / history (optional)")]
    [Tooltip("Key that toggles a simple panel showing the most recent revealed lines. Set to None to disable the feature entirely.")]
    public KeyCode historyToggleKey = KeyCode.Tab;
    [Tooltip("How many of the most recent lines the backlog keeps/shows.")]
    public int historyMaxEntries = 20;
    [Tooltip("Optional: assign your own backlog panel (a RectTransform, inactive by default, with a TMP_Text child somewhere inside it) if you'd rather design it yourself in the Editor. Leave empty and a simple one is built automatically at runtime the first time it's opened.")]
    public RectTransform historyPanel;

    private readonly List<string> _history = new List<string>();
    private TMP_Text _historyText;
    private bool _historyAutoBuildAttempted;

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
        if (historyToggleKey != KeyCode.None && Input.GetKeyDown(historyToggleKey))
        {
            ToggleHistory();
            return;
        }

        if (historyPanel != null && historyPanel.gameObject.activeSelf)
        {
            // Backlog is open -- any click just closes it, and the normal
            // dialogue-advance click below is suppressed so the player
            // can't accidentally skip the line underneath while reading
            // back through history.
            if (Input.GetMouseButtonDown(0)) CloseHistory();
            return;
        }

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

            yield return RevealLine(line);

            // Separate from any inline <wait:...> commands the line's
            // text might already contain -- this always runs once the
            // whole line (all its text AND all its inline commands) has
            // finished, so a line needs no commands at all to stay paced
            // against StageDirector.
            if (waitForAnimationsByDefault)
            {
                _skipRequested = false;
                yield return WaitForAnimations(null);
                _skipRequested = false;
            }
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

    // One piece of a parsed line: either a run of plain text to type, or
    // an inline command to execute at that exact point.
    private struct LinePart
    {
        public bool isCommand;
        public string text;        // set when !isCommand
        public string commandName; // set when isCommand
        public string commandArg;  // set when isCommand; null if the command had no ":arg"
    }

    // The only names this class ever intercepts as commands (besides
    // "name", which is handled separately just above ParseLineParts --
    // see ResolveNameMarkup). Anything else inside "<...>" -- <b>, <i>,
    // <u>, <color=...>, a closing </b>, or just a stray literal "<" the
    // author typed -- is left completely untouched in the text stream, so
    // TextMeshPro's own rich-text renderer (enabled by default) still
    // sees and handles it exactly as if this parser didn't exist. Without
    // this whitelist, EVERY "<...>" would get intercepted and discarded
    // with a warning, silently breaking ordinary TMP rich text like bold.
    private static readonly HashSet<string> KnownCommands =
        new HashSet<string> { "speaker", "speed", "wait", "pause", "page" };

    // Splits a line's raw text (NOT ink tags -- this text is exactly what
    // _story.Continue() returned, before any "# ..." tag content, which
    // ink already strips out into _story.currentTags and which this class
    // no longer reads at all) on "<name>" / "<name:arg>" markers, but only
    // for names in KnownCommands (see above) -- everything else inside
    // angle brackets passes through as ordinary text. An unmatched "<"
    // with no closing ">" nearby is likewise left as ordinary literal
    // text rather than swallowed.
    private List<LinePart> ParseLineParts(string line)
    {
        var parts = new List<LinePart>();
        int i = 0;
        int textStart = 0;

        while (i < line.Length)
        {
            if (line[i] == '<')
            {
                int close = line.IndexOf('>', i + 1);
                if (close < 0) { i++; continue; } // no closing '>' nearby -- just an ordinary '<', keep scanning as text

                string inner = line.Substring(i + 1, close - i - 1);
                int colon = inner.IndexOf(':');
                string name = (colon >= 0 ? inner.Substring(0, colon) : inner).Trim();
                string arg = colon >= 0 ? inner.Substring(colon + 1).Trim() : null;

                if (name == "name")
                {
                    // Resolved immediately into colored text (a database
                    // lookup only, no timing/coroutine dependency) rather
                    // than treated as a command -- see ResolveNameMarkup.
                    if (i > textStart)
                        parts.Add(new LinePart { isCommand = false, text = line.Substring(textStart, i - textStart) });
                    parts.Add(new LinePart { isCommand = false, text = ResolveNameMarkup(arg) });
                    i = close + 1;
                    textStart = i;
                    continue;
                }

                if (!KnownCommands.Contains(name))
                {
                    // Not ours -- leave it untouched (most likely TMP rich
                    // text) and keep scanning past it as plain text.
                    i = close + 1;
                    continue;
                }

                if (i > textStart)
                    parts.Add(new LinePart { isCommand = false, text = line.Substring(textStart, i - textStart) });

                parts.Add(new LinePart { isCommand = true, commandName = name, commandArg = arg });

                i = close + 1;
                textStart = i;
            }
            else
            {
                i++;
            }
        }

        if (textStart < line.Length)
            parts.Add(new LinePart { isCommand = false, text = line.Substring(textStart) });

        return parts;
    }

    // "<name:id>" -- resolves to that character's display name
    // (NpcTemplateDefinition.baseName), wrapped in their configured
    // nameColor (a TMP <color> tag) if one is set in npc_templates.json.
    // Falls back to the raw id itself if "database" isn't wired up or no
    // matching template exists, so a typo'd id is at least visible/
    // debuggable rather than silently vanishing. Doing this at PARSE time
    // (not as a command executed at reveal time) is fine -- it's a pure
    // data lookup with no timing dependency, so the result can just be
    // spliced into the text stream like any other literal text, and it
    // types out (and paginates) exactly like ordinary authored text would.
    private string ResolveNameMarkup(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return "";

        var template = database != null ? database.NpcTemplates?.Find(t => t.id == characterId) : null;
        string displayName = !string.IsNullOrEmpty(template?.baseName) ? template.baseName : characterId;

        if (template != null && !string.IsNullOrEmpty(template.nameColor))
            return $"<color={template.nameColor}>{displayName}</color>";

        return displayName;
    }

    // Runs one inline command. "speaker"/"speed" apply instantly and
    // never pause anything; "wait"/"pause"/"page" all pause the reveal at
    // exactly this point in the text, after which anything following in
    // the SAME line continues on the SAME visual line (RevealLine handles
    // that part -- see its own comment).
    private IEnumerator ExecuteCommand(string name, string arg)
    {
        switch (name)
        {
            case "speaker":
                _currentSpeakerId = arg;
                UpdateSpeakerHighlight();
                break;

            case "speed":
                if (float.TryParse(arg, out var cps)) charsPerSecond = cps;
                break;

            case "wait":
                if (string.IsNullOrEmpty(arg) || arg == "click")
                {
                    yield return WaitForClick();
                }
                else
                {
                    _skipRequested = false;
                    yield return WaitForAnimations(arg == "all" ? null : arg);
                    _skipRequested = false;
                }
                break;

            case "pause":
                if (float.TryParse(arg, out var ms))
                {
                    float seconds = ms / 1000f;
                    _skipRequested = false;
                    float t = 0f;
                    while (t < seconds && !_skipRequested) { t += Time.deltaTime; yield return null; }
                    _skipRequested = false;
                }
                break;

            case "page":
                storyText.text = _pageAccumulated; // in case anything upstream left a stale/overflowing probe value on screen
                yield return WaitForAdvanceClick();
                _pageAccumulated = "";
                break;

            default:
                Debug.LogWarning($"DialogueUIController: unrecognized inline command '<{name}" +
                                  (arg != null ? ":" + arg : "") + ">' in ink text -- ignoring it.");
                break;
        }
    }

    // Top-level entry point for one whole ink line (one _story.Continue()
    // result). Splits it into text/command parts and processes them in
    // order: a text part is typed (via RevealSegment, which may itself
    // span several pages); a command part runs immediately (see
    // ExecuteCommand) and may pause before the next part starts.
    //
    // "firstSegment" tracks whether ANY text has been typed yet for this
    // whole Continue() result: the very first text typed is separated
    // from whatever's already on the page by "\n" (a new ink line is a
    // new paragraph), but every text part after that -- whether preceded
    // by a command or not -- continues directly with no separator, since
    // from here on it's all still visually "the same line" as far as the
    // author's own text and <wait:.../<page> placement says it is.
    //
    // Also accumulates "fullLineText" -- every text part's content
    // concatenated together (pagination/click-waits aside), which is
    // exactly what this ink line rendered as (TMP rich text and any
    // <name:id> substitution included, since those are already baked
    // into the parts by ParseLineParts) -- pushed into the backlog once
    // the whole line is done. Command parts that produce no text (waits,
    // pauses, speaker/speed changes) simply don't contribute anything.
    private IEnumerator RevealLine(string line)
    {
        var parts = ParseLineParts(line);
        bool firstSegment = true;
        string fullLineText = "";

        foreach (var part in parts)
        {
            if (part.isCommand)
            {
                yield return ExecuteCommand(part.commandName, part.commandArg);
            }
            else
            {
                fullLineText += part.text;
                yield return RevealSegment(part.text, firstSegment ? "\n" : "");
                firstSegment = false;
            }
        }

        if (fullLineText.Trim().Length > 0)
        {
            _history.Add(fullLineText);
            while (_history.Count > Mathf.Max(1, historyMaxEntries)) _history.RemoveAt(0);
        }
    }

    // Reveals "segment" onto the dialogue box, deciding page breaks with
    // word-level granularity -- a word is only ever split across pages in
    // the one case where there's no alternative (a single word too long
    // to fit on a blank page by itself). "separator" is what to insert
    // between whatever's already on the page and this segment, IF they
    // end up sharing a page: "\n" for the start of a genuinely new ink
    // line, "" for a same-line continuation (the segment right after a
    // <wait:.../<page> command, or after another text segment).
    //
    // Three rules, checked in order, each only asked once the previous
    // one has already been ruled out:
    //   1. Does the whole remaining chunk fit alongside whatever's
    //      already on this page? If yes, just type it -- this is the
    //      common case, and the overflow check happens exactly ONCE for
    //      it, not once per character.
    //   2. If not, and the page isn't blank yet, clear it and retry rule
    //      1 against a fresh page -- often a blank page is roomy enough
    //      on its own even though it didn't fit alongside prior content.
    //   3. If the remaining chunk still doesn't fit even alone on a
    //      blank page, split at word boundaries: type as many whole
    //      words as fit, clear, and continue with the rest (recursing
    //      through rules 1-3 again for the remainder, so a very long
    //      segment can span more than two pages).
    //   4. Only if a single WORD is itself too long to fit alone on a
    //      blank page does this fall back to splitting that one word by
    //      character -- the only scenario where a word can end up split
    //      across pages, because there's genuinely no better option.
    private IEnumerator RevealSegment(string segment, string separator)
    {
        string remaining = segment;

        while (true)
        {
            string prefix = _pageAccumulated.Length > 0 ? _pageAccumulated + separator : "";

            // Rule 1.
            if (FitsOnPage(prefix + remaining))
            {
                yield return TypeCharacters(prefix, remaining);
                _pageAccumulated = prefix + remaining;
                yield break;
            }

            // Rule 2.
            if (_pageAccumulated.Length > 0)
            {
                storyText.text = _pageAccumulated; // FitsOnPage's probe left an overflowing value on screen -- restore the last good state before waiting
                yield return WaitForAdvanceClick();
                _pageAccumulated = "";
                continue;
            }

            // Rule 3/4. Blank page, and "remaining" alone still doesn't
            // fit. Ask TMP directly which of its own characters land on
            // the first page (a single measurement pass) instead of
            // repeatedly re-measuring growing candidate strings -- see
            // CountCharsOnFirstPage's comment for why that approach
            // turned out to silently accept far more text than actually
            // fit.
            int fitCharCount = CountCharsOnFirstPage(remaining);
            int wordBoundary = LastWordBoundaryAtOrBefore(remaining, fitCharCount);

            string chunk = wordBoundary > 0
                ? remaining.Substring(0, wordBoundary)
                : remaining.Substring(0, Mathf.Max(1, fitCharCount)); // no word boundary within what fits -- a single word longer than the box, split by character instead

            yield return TypeCharacters("", chunk);
            _pageAccumulated = chunk;

            remaining = remaining.Substring(chunk.Length).TrimStart(' ');
            if (remaining.Length == 0) yield break;

            yield return WaitForAdvanceClick();
            _pageAccumulated = "";
        }
    }

    // Reveals "text" one character at a time after "prefix" (already on
    // screen), at charsPerSecond, respecting a mid-typing skip click.
    // Makes no page-overflow decisions itself -- the caller has already
    // guaranteed prefix+text fits on one page before calling this.
    private IEnumerator TypeCharacters(string prefix, string text)
    {
        int shown = 0;
        while (shown < text.Length)
        {
            bool instantJump = _skipRequested; // true = fast-forwarding straight to the end, not a real per-character reveal
            shown = instantJump ? text.Length : shown + 1;
            storyText.text = prefix + text.Substring(0, shown);

            if (!instantJump) PlayTypewriterBlip(text[shown - 1]);

            if (shown < text.Length && !_skipRequested)
                yield return new WaitForSeconds(1f / Mathf.Max(1f, charsPerSecond));
        }
        _skipRequested = false;
    }

    private int _blipCounter;

    // Plays a short, randomly-varied blip for one just-revealed character
    // -- skipped for whitespace, and only every "blipEveryNChars" eligible
    // character actually plays, so typing doesn't become a wall of noise.
    // A no-op if typewriterAudioSource/typewriterBlipClips aren't set.
    private void PlayTypewriterBlip(char justRevealed)
    {
        if (typewriterAudioSource == null || typewriterBlipClips == null || typewriterBlipClips.Length == 0) return;
        if (char.IsWhiteSpace(justRevealed)) return;

        _blipCounter++;
        if (_blipCounter % Mathf.Max(1, blipEveryNChars) != 0) return;

        typewriterAudioSource.pitch = Random.Range(blipPitchRange.x, blipPitchRange.y);
        typewriterAudioSource.PlayOneShot(typewriterBlipClips[Random.Range(0, typewriterBlipClips.Length)]);
    }

    // True if "text" fits within a single TMP page on its own -- i.e.
    // wouldn't be truncated by the text area's current size. Briefly
    // assigns storyText.text as a probe; safe because this never yields,
    // so nothing is ever actually rendered mid-check (only the value
    // storyText.text holds at the end of the frame is ever drawn). A
    // single, one-off measurement like this is reliable; see
    // CountCharsOnFirstPage below for why doing MANY of these in a tight
    // loop turned out not to be.
    private bool FitsOnPage(string text)
    {
        storyText.text = text;
        storyText.ForceMeshUpdate();
        return storyText.textInfo.pageCount <= 1;
    }

    // How many of "text"'s leading characters, laid out alone on a blank
    // page, actually land on the first page -- read straight from TMP's
    // own per-character layout data (characterInfo[i].pageNumber) via ONE
    // measurement pass, rather than the earlier approach of repeatedly
    // growing a candidate string and re-checking FitsOnPage() on it many
    // times per frame. That approach turned out to be unreliable: TMP's
    // page/overflow measurement doesn't consistently recompute across
    // dozens of rapid-fire ForceMeshUpdate() calls in the same frame, so
    // the old loop kept "accepting" words well past where the box
    // actually ends, typing most of a long paragraph off-screen. Reading
    // pageNumber after a single full-text layout pass doesn't have that
    // problem -- it's also literally what TMP's own page-navigation
    // examples use internally for this exact purpose.
    //
    // Compares against the FIRST character's own pageNumber rather than
    // assuming page numbers start at 0 (or 1) -- sidesteps needing to
    // know that indexing convention at all.
    private int CountCharsOnFirstPage(string text)
    {
        storyText.text = text;
        storyText.ForceMeshUpdate();

        var info = storyText.textInfo;
        if (info.characterCount == 0) return 0;

        int firstPage = info.characterInfo[0].pageNumber;
        int count = 0;
        for (int i = 0; i < info.characterCount && i < text.Length; i++)
        {
            if (info.characterInfo[i].pageNumber != firstPage) break;
            count = i + 1;
        }
        return count;
    }

    // Largest index <= maxIndex in "text" that sits right after a space
    // -- i.e. a safe place to cut without splitting a word. Returns 0 if
    // no such boundary exists before maxIndex (a single word longer than
    // whatever fits).
    private int LastWordBoundaryAtOrBefore(string text, int maxIndex)
    {
        int limit = Mathf.Min(maxIndex, text.Length);
        for (int i = limit; i > 0; i--)
        {
            if (text[i - 1] == ' ') return i;
        }
        return 0;
    }

    // Waits for a click (reusing the same "any click that isn't on a
    // Button" detector as typing-skip), then clears the box -- used both
    // for automatic overflow page breaks above and for an author-forced
    // "<page>" command. Distinct from WaitForClick() below, which pauses
    // on a click WITHOUT clearing anything.
    private IEnumerator WaitForAdvanceClick()
    {
        yield return WaitForClick();
        storyText.text = "";
    }

    // "<wait>" / "<wait:click>" -- pause here, keeping the current page
    // exactly as it is, until the player clicks; whatever text follows in
    // the same ink line then continues right where it left off, same
    // page, same line. Use "<page>" instead if you also want the box
    // cleared before continuing.
    //
    // If "autoAdvance" is on, this also resolves on its own after
    // "autoAdvanceDelay" seconds -- a real click still works too, and
    // immediately cuts the wait short either way. This is the ONE place
    // auto-advance hooks in; since every page break (WaitForAdvanceClick)
    // is built on top of this same method, auto-advance covers both
    // automatically, with no separate handling needed. It does NOT touch
    // choices, which are plain button clicks elsewhere and always wait
    // for the player regardless of this setting.
    private IEnumerator WaitForClick()
    {
        _skipRequested = false;
        float t = 0f;
        while (!_skipRequested && !(autoAdvance && t >= autoAdvanceDelay))
        {
            t += Time.deltaTime;
            yield return null;
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

    // --- Backlog / history ---
    //
    // Deliberately simple: a capped list of the most recent lines (see
    // RevealLine), shown in a panel that shrinks its own font size to
    // fit rather than scrolling (TMP's auto-sizing does the work, so
    // there's no ScrollRect/ContentSizeFitter wiring to get right). Good
    // enough to answer "what did they just say" -- if you want a genuine
    // scrollable, unbounded backlog later, that's a bigger separate
    // feature built around this same _history list.
    //
    // Opening/closing doesn't pause whatever the main dialogue coroutine
    // is doing underneath (typing, an animation wait, a timed pause) --
    // it keeps running in real time behind the panel. Worth knowing if
    // you want a true pause-while-reading-backlog behavior later; that
    // would need a checked "paused" flag at each yield point in
    // RevealSegment/TypeCharacters/ExecuteCommand, which isn't done here.

    private void ToggleHistory()
    {
        EnsureHistoryPanelBuilt();
        if (historyPanel == null) return;

        bool nowOpen = !historyPanel.gameObject.activeSelf;
        if (nowOpen) RefreshHistoryText();
        historyPanel.gameObject.SetActive(nowOpen);
    }

    private void CloseHistory()
    {
        if (historyPanel != null) historyPanel.gameObject.SetActive(false);
    }

    private void RefreshHistoryText()
    {
        if (_historyText != null) _historyText.text = string.Join("\n\n", _history);
    }

    // If "historyPanel" wasn't assigned in the Inspector, builds a plain
    // full-screen-ish overlay with a single auto-sizing TMP_Text child,
    // parented under the same Canvas this controller's own UI lives in.
    // Runs once (via _historyAutoBuildAttempted) even if it fails (e.g.
    // no parent Canvas), so a misconfiguration doesn't retry every frame.
    private void EnsureHistoryPanelBuilt()
    {
        if (historyPanel != null)
        {
            if (_historyText == null) _historyText = historyPanel.GetComponentInChildren<TMP_Text>(true);
            return;
        }
        if (_historyAutoBuildAttempted) return;
        _historyAutoBuildAttempted = true;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("DialogueUIController: no parent Canvas found -- can't auto-build a backlog panel. Assign 'historyPanel' manually instead.");
            return;
        }

        var panelGO = new GameObject("AutoBacklogPanel", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(canvas.transform, false);
        panelGO.transform.SetAsLastSibling(); // draw on top of everything else in the canvas

        var panelRect = (RectTransform)panelGO.transform;
        panelRect.anchorMin = new Vector2(0.05f, 0.05f);
        panelRect.anchorMax = new Vector2(0.95f, 0.95f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.92f);

        var textGO = new GameObject("HistoryText", typeof(RectTransform));
        textGO.transform.SetParent(panelGO.transform, false);
        var textRect = (RectTransform)textGO.transform;
        textRect.anchorMin = new Vector2(0.03f, 0.03f);
        textRect.anchorMax = new Vector2(0.97f, 0.97f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        _historyText = textGO.AddComponent<TextMeshProUGUI>();
        _historyText.color = storyText != null ? storyText.color : Color.white;
        _historyText.enableWordWrapping = true;
        float baseSize = storyText != null ? storyText.fontSize : 28f;
        _historyText.enableAutoSizing = true; // shrinks to fit however many entries are in the log, rather than overflowing or needing a scroll view
        _historyText.fontSizeMin = 12f;
        _historyText.fontSizeMax = baseSize;

        historyPanel = panelRect;
        historyPanel.gameObject.SetActive(false);
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
