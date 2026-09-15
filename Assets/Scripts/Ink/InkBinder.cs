using Ink.Runtime;
using UnityEngine;

// The bridge between an ink Story and the C# WorldState. Every EXTERNAL
// function declared in main.ink must have a matching binding here, with a
// matching name and parameter count/order. If you add a new EXTERNAL line
// in ink, add its binding here too -- the two files have to stay in sync
// by hand, there's no automatic check for that at the point of writing
// either file. GameBootstrapper does call story.ValidateExternalBindings()
// right after Bind() runs, though, which throws immediately (with a clear
// message naming the missing function) if the two ever drift apart --
// that's the actual safety net, this comment is just a reminder while
// you're editing.
public class InkBinder : MonoBehaviour
{
    public WorldState World;

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
    }
}
