using System;
using System.Collections.Generic;
using UnityEngine;

// Small JSON-friendly value types. Kept separate from UnityEngine.Vector2
// so Newtonsoft has an unambiguous plain class to deserialize into --
// convert to Vector2 only where Unity code actually needs one.
[Serializable]
public class Vec2Data
{
    public float x;
    public float y;
    public Vector2 ToVector2() => new Vector2(x, y);
}

[Serializable]
public class SizeData
{
    public float width;
    public float height;
    public Vector2 ToVector2() => new Vector2(width, height);
}

// One character's placement in a given scene. "sprite" is a name, not a
// path -- it's looked up under Resources/Portraits/ at runtime (see
// GameBootstrapper). Anything about a character that might need to change
// mid-scene later (mood, outfit, position) belongs on a class like this
// one, since it's already the single place that describes "what's true
// about this character right now."
[Serializable]
public class CharacterPlacement
{
    public string id;
    public string displayName;
    public string sprite;
    public Vec2Data position = new Vec2Data();
    public SizeData size = new SizeData { width = 500, height = 900 };
}

// One scene: a background, a cast of characters, which ink file drives the
// dialogue, and which shared layout file styles the dialogue UI. Add new
// fields here freely as the design grows (e.g. music, a default mood per
// character, an ambient sound) -- every field is optional to add without
// breaking older scene files, since JsonConvert just leaves unknown/missing
// fields at their default value.
[Serializable]
public class SceneDefinition
{
    public string sceneId;
    public string background;
    public string inkFile;
    public string dialogueLayout;
    public List<CharacterPlacement> characters = new List<CharacterPlacement>();
}
