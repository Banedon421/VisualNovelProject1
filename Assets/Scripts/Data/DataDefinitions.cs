using System;
using System.Collections.Generic;

// Design-time data shapes: what clans/poles/NPC templates exist. These are
// loaded from plain JSON files in Assets/StreamingAssets/Data/ (see
// GameDatabase.cs) rather than hand-filled in the Unity Inspector, so
// either of you can edit them in a text editor.

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
// fallback by GameBootstrapper: if a scene's CharacterPlacement doesn't
// specify displayName/sprite for this id, these values are used instead --
// so "Dupont is called Dupont and defaults to his neutral portrait" only
// needs to be said once, not repeated in every scene file he appears in.
// A scene can still override either field (e.g. a mood-specific sprite for
// one particular beat) by simply specifying it directly.
[Serializable]
public class NpcTemplateDefinition
{
    public string id;
    public string role;         // e.g. "blacksmith", "farmer"
    public string baseName;     // fallback for CharacterPlacement.displayName
    public string defaultSprite; // fallback for CharacterPlacement.sprite
}
