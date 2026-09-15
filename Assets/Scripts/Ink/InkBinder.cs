using Ink.Runtime;
using UnityEngine;

// The bridge between an ink Story and the C# WorldState/StageDirector.
// Every EXTERNAL function declared in an .ink file must have a matching
// binding here, with a matching name and parameter count/order. If you add
// a new EXTERNAL line in ink, add its binding here too -- the two files
// have to stay in sync by hand, there's no automatic check for that at the
// point of writing either file. GameBootstrapper does call
// story.ValidateExternalBindings() right after Bind() runs, though, which
// throws immediately (with a clear message naming the missing function) if
// the two ever drift apart -- that's the actual safety net, this comment
// is just a reminder while you're editing.
public class InkBinder : MonoBehaviour
{
    public WorldState World;
    public StageDirector Stage;

    public void Bind(Story story)
    {
        story.BindExternalFunction("get_stat", (string statId) => World.Stats.Get(statId));
        story.BindExternalFunction("add_stat", (string statId, float amount) => World.Stats.Add(statId, amount));

        story.BindExternalFunction("get_relationship", (string targetId) => World.Relationships.Get(targetId));
        story.BindExternalFunction("add_relationship", (string targetId, float amount) => World.Relationships.Add(targetId, amount));

        story.BindExternalFunction("get_clan", () => World.Player.clanId);
        story.BindExternalFunction("get_pole", () => World.Player.currentPoleId);
        // Routed through WorldState.SetPole rather than assigning directly,
        // so a pole id ink sends that doesn't match poles.json gets a
        // loud warning instead of silently making every get_pole()
        // comparison fall through to "else" with no clue why.
        story.BindExternalFunction("set_pole", (string poleId) => World.SetPole(poleId));

        story.BindExternalFunction("has_flag", (string flagId) => World.History.HasFlag(flagId));
        story.BindExternalFunction("set_flag", (string flagId) => World.History.SetFlag(flagId));

        story.BindExternalFunction("get_year", () => (float)World.Clock.CurrentYear);
        story.BindExternalFunction("advance_years", (float years) => World.Clock.AdvanceYears(Mathf.RoundToInt(years)));

        // --- Stage control (StageDirector) ---
        // All of these are fire-and-forget: they kick off (or cancel and
        // restart) a coroutine on StageDirector and return immediately.
        // Ink's own Continue() never waits on them, so a character can
        // still be mid-slide while the next line of dialogue prints.
        story.BindExternalFunction("jump_to", (string id, string position) => Stage.JumpTo(id, position, animated: true));
        story.BindExternalFunction("jump_to_instant", (string id, string position) => Stage.JumpTo(id, position, animated: false));
        story.BindExternalFunction("enter", (string id, string position) => Stage.Enter(id, position));
        story.BindExternalFunction("exit", (string id) => Stage.Exit(id));
        story.BindExternalFunction("turn_around", (string id) => Stage.TurnAround(id));
        story.BindExternalFunction("face", (string id, string direction) => Stage.Face(id, direction));
        story.BindExternalFunction("switch_mood", (string id, string mood) => Stage.SwitchMood(id, mood));
        story.BindExternalFunction("highlight", (string id) => Stage.Highlight(id));
        story.BindExternalFunction("set_depth", (string id, float order) => Stage.SetDepth(id, Mathf.RoundToInt(order)));

        // Generic transparency control. "duration" is in SECONDS (0.5 =
        // 500ms) for consistency with moveDuration/fadeDuration -- seconds
        // stay correct regardless of framerate, a frame count wouldn't.
        story.BindExternalFunction("fade_to", (string id, float alpha, float duration) => Stage.FadeTo(id, alpha, duration));
        story.BindExternalFunction("fade_in", (string id) => Stage.FadeIn(id));
        story.BindExternalFunction("fade_out", (string id) => Stage.FadeOut(id));
    }
}
