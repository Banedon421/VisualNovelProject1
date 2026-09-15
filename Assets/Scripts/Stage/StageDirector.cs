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
public class StageDirector : MonoBehaviour
{
    [Tooltip("Duration in seconds for an animated jump_to move or a highlight pop.")]
    public float moveDuration = 0.6f;
    [Tooltip("Default duration in seconds for enter/exit and fade_in/fade_out.")]
    public float fadeDuration = 0.35f;

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
        // Scene-specific entries add new names or override a global one of
        // the same id -- e.g. town_square could define a "well" position
        // no other scene needs, without editing the shared positions.json.
        if (sceneOverrides != null)
            foreach (var p in sceneOverrides) _positions[p.id] = p;
    }

    // --- Initial cast, called once by GameBootstrapper at scene load ---

    public void SpawnInitialCast(List<CharacterPlacement> placements)
    {
        for (int i = _container.childCount - 1; i >= 0; i--)
            Destroy(_container.GetChild(i).gameObject);

        // Stop any leftover per-character queues from a previous scene --
        // same reload-safety reasoning as GameBootstrapper's InkBinder
        // destroy-first pattern. Not reachable yet since Start() only runs
        // once today, but cheap to get right now rather than leave as a
        // trap for whenever scene-switching exists.
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
    // Every one of these does its "can this even happen" validation
    // IMMEDIATELY (so a typo'd id/position is reported right when ink
    // calls it), then enqueues the actual work to run in this
    // character's turn.

    public void JumpTo(string id, string positionName, bool animated)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "JumpTo"); return; }
        if (!TryGetPosition(positionName, out var pos)) return;

        var rect = view.portraitImage.rectTransform;
        Vector2 targetAnchor = pos.anchor.ToVector2();

        // null = "no size defined for this position, keep whatever size
        // the character already is and just move them."
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

        // Spawning happens immediately (not queued) so _onScreen reflects
        // reality right away -- a jump_to/highlight/etc. call on this same
        // id on the very next ink line can find it. Only the fade-in
        // VISUAL is queued.
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

        // Removed from _onScreen immediately -- once exit() has been
        // called, this character is "gone" for every subsequent call even
        // while its fade-out is still queued/playing. That's the least
        // surprising behavior: nothing can jump_to/highlight/etc. someone
        // who is already leaving, even mid-fade.
        _onScreen.Remove(id);
        EnqueueAnim(id, ExitRoutine(view, fadeDuration));
    }

    public void TurnAround(string id)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "TurnAround"); return; }
        EnqueueAnim(id, InstantRoutine(() => { view.facingRight = !view.facingRight; ApplyFacing(view); }));
    }

    public void Face(string id, string direction)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "Face"); return; }
        EnqueueAnim(id, InstantRoutine(() => { view.facingRight = direction != "left"; ApplyFacing(view); }));
    }

    private void ApplyFacing(CharacterView view)
    {
        var t = view.portraitImage.rectTransform;
        var scale = t.localScale;
        float mag = Mathf.Abs(scale.x);
        t.localScale = new Vector3(view.facingRight ? mag : -mag, scale.y, scale.z);
    }

    public void SwitchMood(string id, string mood)
    {
        if (!_onScreen.TryGetValue(id, out var view)) { WarnNotOnScreen(id, "SwitchMood"); return; }
        EnqueueAnim(id, InstantRoutine(() =>
        {
            // Convention: mood variants live next to the default portrait
            // using the same "{id}_{mood}" naming npc_templates.json
            // already uses for defaultSprite (dupont_neutral.jpg ->
            // dupont_worried.jpg is just adding a file, no config needed).
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

    // Generic transparency control -- targetAlpha 0..1, duration in
    // SECONDS (not frames: seconds stay correct regardless of framerate,
    // frame counts don't -- 500ms is just 0.5). fade_in/fade_out are
    // thin convenience wrappers using the same default fadeDuration
    // enter/exit already use.
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
            // Waits for this action to fully finish -- including its own
            // internal yields -- before the loop dequeues the next one.
            // This is the entire ordering guarantee described at the top
            // of the class, and it costs nothing for "instant" actions
            // (InstantRoutine/SnapRoutine yield break immediately, so this
            // just falls through to the next queued item in the same frame).
            yield return StartCoroutine(action);
        }
        _runners[id] = null;
    }

    // --- Individual animations. Each reads whatever state it needs
    // (current anchor, current alpha, current scale) INSIDE the
    // coroutine body rather than from a value captured when it was
    // enqueued -- C# iterator methods don't run any code until actually
    // started, so this naturally reads "current" state at the moment
    // this action's turn in the queue arrives, not at enqueue time. ---

    private static IEnumerator InstantRoutine(System.Action action)
    {
        action();
        yield break;
    }

    // Interpolates from the character's CURRENT effective anchor --
    // computed from its existing anchor + anchoredPosition, not just
    // anchorMin -- so this works correctly even for a character still
    // sitting in legacy pixel-offset mode (anchor 0.5,0.5 + a pixel
    // offset), not only for ones already placed via a named anchor.
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

    // "to" only -- "from" is read from the Image's CURRENT alpha the
    // moment this routine actually starts (see the class-level note
    // above), which is what makes fade_to/fade_in/fade_out, enter's
    // fade-in, and exit's fade-out all compose correctly through the
    // same queue without needing to pre-compute a starting value.
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
}
