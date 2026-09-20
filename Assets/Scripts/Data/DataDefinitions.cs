using System;
using System.Collections.Generic;

// Design-time data shapes: what clans/poles/NPC templates/stage positions
// exist. These are loaded from plain JSON files in
// Assets/StreamingAssets/Data/ (see GameDatabase.cs) rather than
// hand-filled in the Unity Inspector, so either of you can edit them in a
// text editor.

[Serializable]
public class ClanDefinition
{
    public string id;
    public string displayName;
    public string description;
    public List<string> traits = new List<string>();
    // Applied once, at character creation, on top of each stat's default.
    public Dictionary<string, float> startingStatModifiers = new Dictionary<string, float>();
}

[Serializable]
public class PoleDefinition
{
    public string id;
    public string displayName;
    public string description;
}

// A recurring NPC's base identity, independent of any one scene. Used as a
// fallback by StageDirector: if a scene's CharacterPlacement (or an
// enter() call) doesn't specify displayName/sprite for this id, these
// values are used instead -- so "Dupont is called Dupont and defaults to
// his neutral portrait" only needs to be said once, not repeated in every
// scene file or every enter() call. A scene can still override either
// field (e.g. a mood-specific sprite for one particular beat) by simply
// specifying it directly.
[Serializable]
public class NpcTemplateDefinition
{
    public string id;
    public string role;         // e.g. "blacksmith", "farmer"
    public string baseName;     // fallback for CharacterPlacement.displayName
    public string defaultSprite; // fallback for CharacterPlacement.sprite

    // Optional TMP color string (e.g. "#E0A030FF" or a named color like
    // "orange"), used by DialogueUIController's "<name:id>" inline
    // command to auto-color this character's displayed name wherever
    // it's used in dialogue, instead of hand-wrapping <color=...> around
    // it in every line. Leave "" for a plain, uncolored name.
    public string nameColor = "";
}

// A named on-screen spot, shared across every scene unless a specific
// scene overrides/extends it (see SceneDefinition.positionOverrides).
// "anchor" is a normalized (0..1, or outside that range for off-screen
// staging spots) fraction of CharactersContainer -- same coordinate space
// CharacterPlacement.anchor already uses, just given a name so ink can
// say jump_to("dupont", "right") instead of raw numbers.
// "sizeNormalized" is optional -- if a position doesn't set it (rare;
// most should), StageDirector.JumpTo keeps whatever size the character
// already is rather than resizing them.
[Serializable]
public class PositionDefinition
{
    public string id;
    public Vec2Data anchor;
    public SizeData sizeNormalized;
}
