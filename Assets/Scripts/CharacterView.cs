using UnityEngine;
using UnityEngine.UI;

// Attached automatically to every character portrait, whether placed by
// GameBootstrapper's initial scene load or spawned later by StageDirector
// (enter). Started as a pure data holder; now also carries the one bit of
// state StageDirector needs to keep turn_around/face consistent across
// repeated calls -- name label placement, mood history, etc. can still
// hang off this later the same way.
public class CharacterView : MonoBehaviour
{
    public string characterId;
    public string displayName;
    public Image portraitImage;

    // True = facing the "default" orientation the source art was drawn in
    // (an arbitrary baseline -- doesn't itself assume what "left"/"right"
    // means on screen). StageDirector.TurnAround/Face both read AND write
    // this rather than inferring facing from the RectTransform's current
    // scale sign, so it stays correct even across an intervening scale
    // change that only ever animates magnitude, never sign (e.g. a
    // Highlight pop).
    public bool facingRight = true;
}
