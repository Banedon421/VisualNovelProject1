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

// One character's placement in a given scene. "displayName" and "sprite"
// are optional overrides -- if left out, StageDirector falls back to the
// matching NpcTemplateDefinition (looked up by "id") from
// npc_templates.json, so a recurring character's name/default look isn't
// repeated in every scene file that uses them.
//
// POSITIONING: two modes, chosen per-character by which fields the JSON
// sets. Both are read by StageDirector.SpawnCharacter.
//
//   - NORMALIZED (preferred for new scenes, and REQUIRED if you want this
//     character to later respond to jump_to/enter/exit from ink): set
//     "anchor" (0..1 fraction of the CharactersContainer, x left->right,
//     y bottom->top) and optionally "sizeNormalized" (0..1 fraction of
//     the container's width/height). These are fractions of the
//     container rather than fixed pixel numbers, so a character placed
//     this way stays proportionally in the same spot and the same
//     relative size no matter what aspect ratio Canvas Scaler resolves
//     to -- Unity's own RectTransform anchor system does this, no C#
//     scale-factor math needed anywhere.
//   - LEGACY PIXEL (still supported -- what town_square.json and
//     village_intro.json currently use): set "position" (pixel offset
//     from the container's center, in Reference Resolution units) and
//     "size" (pixel width/height). Movement functions (jump_to etc.)
//     still work on a legacy-placed character -- StageDirector converts
//     its current pixel offset to an equivalent anchor fraction before
//     animating -- but the character won't adapt to a different aspect
//     ratio until the first time it's moved.
//
// If "anchor" is present it wins, and "position"/"size" are ignored for
// that character. Mixing modes across different characters in the same
// scene is fine.
[Serializable]
public class CharacterPlacement
{
    public string id;
    public string displayName;
    public string sprite;

    // Normalized mode (optional -- null if the JSON doesn't set it).
    public Vec2Data anchor;
    public SizeData sizeNormalized;

    // Legacy pixel mode (defaults keep old scene files working unchanged).
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

    // Optional named positions specific to this scene -- merged on top of
    // the shared StreamingAssets/Data/positions.json (same id = override,
    // new id = addition). Leave null/omitted for scenes that only need the
    // shared "left"/"center"/"right"/"off_left"/"off_right" set.
    public List<PositionDefinition> positionOverrides;
}
