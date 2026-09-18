using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Owns "who is currently on screen and where," for anything beyond the
// scene's initial layout: ink-driven movement (jump_to/jump_to_instant),
// turning (turn_around/face), mood swaps (switch_mood), bringing
// characters on/off mid-scene (enter/exit), transparency (fade_to/
// fade_in/fade_out), a speaking-emphasis pop (highlight), and draw order
// (set_depth). GameBootstrapper still reads the scene JSON and hands the
// initial cast to SpawnInitialCast; everything after that is driven by
// InkBinder calling into this class from ink EXTERNAL functions.
//
// ORDERING GUARANTEE: every instruction for a given character (move,
// turn, mood swap, fade, enter, exit...) runs strictly in the order ink
// called it, each one completing fully before the next starts for THAT
// character -- so turn_around() immediately followed by exit() always
// finishes the turn before the exit fade begins, and a character is
// never left mid-slide because a new instruction arrived. This is
// enforced entirely PER CHARACTER via a small FIFO queue (see
// EnqueueAnim/RunQueue below): two different characters' queues run
// independently and in parallel, so moving several characters "at once"
// is completely unaffected by this.
//
// Everything here is otherwise "fire and forget" from ink's perspective
// -- ink's own Continue() never waits on any of this, so a character can
// still be mid-slide while the next line of dialogue is already printing.
// DialogueUIController can optionally pace itself against this via
// IsAnimating/IsAnyAnimating below (see its "wait" tags / default-wait
// behavior) -- that's a read-only query, it doesn't change anything here.
public class StageDirector : MonoBehaviour
{
    [Tooltip("Duration in seconds for an animated jump_to move or a highlight pop.")]
    public float moveDuration = 0.6f;
    [Tooltip("Default duration in seconds for enter/exit and fade_in/fade_out.")]
    public float fadeDuration = 0.35f;
    [Tooltip("Duration in seconds for a turn_around/face flip (the compress-flip-grow animation).")]
    public float turnDuration = 0.4f;

    private RectTransform _container;
    private Image _characterPrefab;
    private GameDatabase _database;
    private readonly Dictionary<string, PositionDefinition> _positions = new Dictionary<string, PositionDefinition>();
    private readonly Dictionary<string, CharacterView> _onScreen = new Dictionary<string, CharacterView>();

    // Per-character FIFO queue of pending instructions, plus the single
    // coroutine currently draining that queue (null/absent when idle).
    private readonly Dictionary<string, Queue<IEnumerator>> _queues = new Dictionary<string, Queue<IEnumerator>>();
    private readonly Dictionary<string, Coroutine> _runners = new Dictionary<string, Coroutine>();

    public void Initialize(RectTransform container, Image characterPrefab, GameDatabase database,
        List<PositionDefinition> globalPositions, List<PositionDefinition> sceneOverrides)
    {
        _container = container;
        _characterPrefab = characterPrefab;
        _database = database;

        _positions.Clear();
        if (globalPositions != null)
            foreach (var p in globalPositions) _positions[p.id] = p;
        if (sceneOverrides != null)
            foreach (var p in sceneOverrides) _positions[p.id] = p;
    }

    // --- Initial cast, called once by GameBootstrapper at scene load ---

    public void SpawnInitialCast(List<CharacterPlacement> placements)
    {
        for (int i = _container.childCount - 1; i >= 0; i--)
            Destroy(_container.GetChild(i).gameObject);

        foreach (var runner in _runners.Values)
            if (runner != null) StopCoroutine(runner);
        _queues.Clear();
        _runners.Clear();
        _onScreen.Clear();

        if (placements == null) return;

        foreach (var placement in placements)
        {
            var view = SpawnCharacter(placement.id, placement.displayName, placement.sprite,
                placement.anchor, placement.sizeNormalized, placement.position, placement.size);
            if (view != null) _onScreen[placement.id] = view;
        }
    }

    private CharacterView SpawnCharacter(string id, string displayNameOverride, string spriteOverride,
        Vec2Data anchor, SizeData sizeNormalized, Vec2Data legacyPosition, SizeData legacySize)
    {
        var instance = Instantiate(_characterPrefab, _container);
        instance.name = "Character_" + id;

        var rect = instance.rectTransform;
        Rect containerRect = _container.rect;

        if (anchor != null)
        {
            rect.anchorMin = rect.anchorMax = anchor.ToVector2();
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = sizeNormalized != null
                ? new Vector2(sizeNormalized.width * containerRect.width, sizeNormalized.height * containerRect.height)
                : legacySize.ToVector2();
        }
        else
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = legacyPosition.ToVector2();
            rect.sizeDelta = legacySize.ToVector2();
        }

        var template = _database.NpcTemplates?.Find(t => t.id == id);
        string displayName = !string.IsNullOrEmpty(displayNameOverride) ? displayNameOverride : template?.baseName;
        string spriteName = !string.IsNullOrEmpty(spriteOverride) ? spriteOverride : template?.defaultSprite;

        ApplySprite(instance, id, spriteName);

        var view = instance.gameObject.AddComponent<CharacterView>();
        view.characterId = id;
        view.displayName = displayName;
        view.portraitImage = instance;
        view.facingRight = true;
        return view;
    }

    private void ApplySprite(Image target, string id, string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName))
        {
            Debug.LogWarning($"StageDirector: character '{id}' has no sprite name (not set in scene JSON and no npc_templates.json entry).");
            return;
        }
        var sprite = Resources.Load<Sprite>("Portraits/" + spriteName);
        if (sprite == null)
        {
            Debug.LogError($"StageDirector: no portrait sprite found for '{id}' (tried Resources/Portraits/{spriteName}).");
            return;
        }
        target.sprite = sprite;
    }

    // --- Ink-facing operations, called by InkBinder ---

    public void JumpTo(string id, string positionName, bool animated)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "JumpTo"); return; }
        if (!TryGetPosition(positionName, out var pos)) return;

        var rect = view.portraitImage.rectTransform;
        Vector2 targetAnchor = pos.anchor.ToVector2();

        Vector2? targetSize = null;
        if (pos.sizeNormalized != null)
        {
            Rect containerRect = _container.rect;
            targetSize = new Vector2(pos.sizeNormalized.width * containerRect.width, pos.sizeNormalized.height * containerRect.height);
        }

        EnqueueAnim(id, animated
            ? MoveRoutine(rect, _container, targetAnchor, targetSize, moveDuration)
            : SnapRoutine(rect, targetAnchor, targetSize));
    }

    public void Enter(string id, string positionName)
    {
        if (_onScreen.ContainsKey(id))
        {
            Debug.LogWarning($"StageDirector.Enter: '{id}' is already on screen -- use jump_to() to move them instead.");
            return;
        }
        if (!TryGetPosition(positionName, out var pos)) return;

        var view = SpawnCharacter(id, null, null, pos.anchor, pos.sizeNormalized, new Vec2Data(), new SizeData { width = 500, height = 900 });
        _onScreen[id] = view;

        var c = view.portraitImage.color;
        c.a = 0f;
        view.portraitImage.color = c;
        EnqueueAnim(id, FadeRoutine(view.portraitImage, 1f, fadeDuration));
    }

    public void Exit(string id)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "Exit"); return; }

        _onScreen.Remove(id);
        EnqueueAnim(id, ExitRoutine(view, fadeDuration));
    }

    public void TurnAround(string id)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "TurnAround"); return; }
        // null target = toggle whatever facingRight actually is when this
        // action's turn in the queue arrives (see TurnRoutine).
        EnqueueAnim(id, TurnRoutine(view, null, turnDuration));
    }

    public void Face(string id, string direction)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "Face"); return; }
        EnqueueAnim(id, TurnRoutine(view, direction != "left", turnDuration));
    }

    public void SwitchMood(string id, string mood)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "SwitchMood"); return; }
        EnqueueAnim(id, InstantRoutine(() =>
        {
            string spriteName = $"{id}_{mood}";
            var sprite = Resources.Load<Sprite>("Portraits/" + spriteName);
            if (sprite == null)
                Debug.LogWarning($"StageDirector.SwitchMood: no sprite at Resources/Portraits/{spriteName} -- keeping the current portrait.");
            else
                view.portraitImage.sprite = sprite;
        }));
    }

    public void Highlight(string id)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "Highlight"); return; }
        EnqueueAnim(id, HighlightRoutine(view.portraitImage.rectTransform, moveDuration));
    }

    public void SetDepth(string id, int order)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "SetDepth"); return; }
        EnqueueAnim(id, InstantRoutine(() =>
        {
            int clamped = Mathf.Clamp(order, 0, Mathf.Max(0, _container.childCount - 1));
            view.portraitImage.rectTransform.SetSiblingIndex(clamped);
        }));
    }

    public void FadeTo(string id, float targetAlpha, float duration)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "FadeTo"); return; }
        EnqueueAnim(id, FadeRoutine(view.portraitImage, Mathf.Clamp01(targetAlpha), Mathf.Max(0f, duration)));
    }

    public void FadeIn(string id) => FadeTo(id, 1f, fadeDuration);
    public void FadeOut(string id) => FadeTo(id, 0f, fadeDuration);

    private bool TryGetPosition(string positionName, out PositionDefinition pos)
    {
        if (_positions.TryGetValue(positionName, out pos)) return true;
        Debug.LogWarning($"StageDirector: no position named '{positionName}' -- known positions: " +
                          string.Join(", ", _positions.Keys));
        return false;
    }

    private static void WarnNotOnScreen(string id, string op) =>
        Debug.LogWarning($"StageDirector.{op}: '{id}' isn't on screen -- call enter() first (or check the id for a typo).");

    // --- Per-character queue ---

    private void EnqueueAnim(string id, IEnumerator action)
    {
        if (!_queues.TryGetValue(id, out var queue))
        {
            queue = new Queue<IEnumerator>();
            _queues[id] = queue;
        }
        queue.Enqueue(action);

        if (!_runners.TryGetValue(id, out var runner) || runner == null)
        {
            _runners[id] = StartCoroutine(RunQueue(id));
        }
    }

    private IEnumerator RunQueue(string id)
    {
        while (_queues.TryGetValue(id, out var queue) && queue.Count > 0)
        {
            var action = queue.Dequeue();
            yield return StartCoroutine(action);
        }
        _runners[id] = null;
    }

    // Queried by DialogueUIController to pace text against animations
    // (its "wait:"/"pause:" tags and default-wait behavior). A character
    // counts as animating whenever its queue still has a runner coroutine
    // actively draining it -- RunQueue clears the runner to null the
    // instant the queue empties, so this is always current, no extra
    // bookkeeping needed.
    public bool IsAnimating(string id) => _runners.TryGetValue(id, out var runner) && runner != null;

    public bool IsAnyAnimating()
    {
        foreach (var runner in _runners.Values)
            if (runner != null) return true;
        return false;
    }

    // --- Individual animations ---

    private static IEnumerator InstantRoutine(System.Action action)
    {
        action();
        yield break;
    }

    private static IEnumerator MoveRoutine(RectTransform rect, RectTransform container, Vector2 targetAnchor, Vector2? targetSize, float duration)
    {
        Rect containerRect = container.rect;
        Vector2 startAnchor = rect.anchorMin + new Vector2(
            containerRect.width > 0f ? rect.anchoredPosition.x / containerRect.width : 0f,
            containerRect.height > 0f ? rect.anchoredPosition.y / containerRect.height : 0f);
        Vector2 startSize = rect.sizeDelta;
        Vector2 endSize = targetSize ?? startSize;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            rect.anchorMin = rect.anchorMax = Vector2.Lerp(startAnchor, targetAnchor, p);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.Lerp(startSize, endSize, p);
            yield return null;
        }
        rect.anchorMin = rect.anchorMax = targetAnchor;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = endSize;
    }

    private static IEnumerator SnapRoutine(RectTransform rect, Vector2 targetAnchor, Vector2? targetSize)
    {
        rect.anchorMin = rect.anchorMax = targetAnchor;
        rect.anchoredPosition = Vector2.zero;
        if (targetSize.HasValue) rect.sizeDelta = targetSize.Value;
        yield break;
    }

    private static IEnumerator FadeRoutine(Image img, float to, float duration)
    {
        float from = img.color.a;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            var c = img.color;
            c.a = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            img.color = c;
            yield return null;
        }
        var final = img.color;
        final.a = to;
        img.color = final;
    }

    private IEnumerator ExitRoutine(CharacterView view, float duration)
    {
        yield return FadeRoutine(view.portraitImage, 0f, duration);
        if (view != null && view.gameObject != null) Destroy(view.gameObject);
    }

    private static IEnumerator HighlightRoutine(RectTransform rect, float duration)
    {
        Vector3 baseScale = rect.localScale;
        float half = duration * 0.5f;
        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / half));
            rect.localScale = Vector3.LerpUnclamped(baseScale, baseScale * 1.08f, p);
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / half));
            rect.localScale = Vector3.LerpUnclamped(baseScale * 1.08f, baseScale, p);
            yield return null;
        }
        rect.localScale = baseScale;
    }

    // Compresses the portrait horizontally to zero width, flips the
    // facing flag (and the underlying scale sign) while nothing is
    // visible, then grows back out to full width facing the new
    // direction -- giving the illusion of a continuous horizontal flip
    // instead of an instant pop.
    //
    // targetFacingRight == null means "toggle" (TurnAround): resolved
    // HERE, when this action's turn in the queue actually arrives, not
    // when TurnAround/Face was called -- same "read current state inside
    // the coroutine" principle as every other routine in this class. This
    // matters because a toggle has to toggle whatever facingRight actually
    // is at that moment, not whatever it was several queued instructions
    // ago.
    private static IEnumerator TurnRoutine(CharacterView view, bool? targetFacingRight, float duration)
    {
        bool target = targetFacingRight ?? !view.facingRight;
        if (view.facingRight == target) yield break; // already facing that way -- nothing to animate

        var rect = view.portraitImage.rectTransform;
        Vector3 baseScale = rect.localScale;
        float mag = Mathf.Abs(baseScale.x);
        float startSign = view.facingRight ? 1f : -1f;
        float endSign = target ? 1f : -1f;
        float half = duration * 0.5f;

        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / half);
            rect.localScale = new Vector3(Mathf.Lerp(mag, 0f, p) * startSign, baseScale.y, baseScale.z);
            yield return null;
        }

        view.facingRight = target;
        rect.localScale = new Vector3(0f, baseScale.y, baseScale.z);

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / half);
            rect.localScale = new Vector3(Mathf.Lerp(0f, mag, p) * endSign, baseScale.y, baseScale.z);
            yield return null;
        }
        rect.localScale = new Vector3(mag * endSign, baseScale.y, baseScale.z);
    }
}
