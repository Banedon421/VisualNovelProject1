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

[Serializable]
public class NpcTemplateDefinition
{
    public string id;
    public string role;      // e.g. "blacksmith", "farmer"
    public string baseName;  // fallback name if no clan-specific variant is written
}
