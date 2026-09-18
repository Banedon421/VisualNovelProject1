using System;

// Styling/layout for the dialogue UI, shared across every scene of the
// "visual novel dialogue" type rather than repeated per scene. Anchors are
// plain 0-1 normalized values -- Unity's own RectTransform anchor system is
// already "relative to screen size" once the Canvas Scaler is set up (see
// setup notes), so this reuses that instead of inventing a second scaling
// concept. fontSize is in the same reference-resolution units as the
// Canvas Scaler, so it scales consistently with everything else.
[Serializable]
public class DialogueLayoutConfig
{
    public Vec2Data textAreaAnchorMin = new Vec2Data { x = 0f, y = 0.55f };
    public Vec2Data textAreaAnchorMax = new Vec2Data { x = 1f, y = 1f };

    public Vec2Data choiceAreaAnchorMin = new Vec2Data { x = 0f, y = 0f };
    public Vec2Data choiceAreaAnchorMax = new Vec2Data { x = 1f, y = 0.5f };

    public float fontSize = 36f;

    // Hex colors, RGBA, e.g. "#FFFFFFFF" opaque white, "#00000099" translucent black.
    public string textColor = "#FFFFFFFF";

    // Dialogue panel tint. Multiplies onto a plain WHITE 9-slice sprite
    // (see panelBorderSprite below) -- the sprite carries no baked-in
    // color, only shape plus a fill/border alpha difference, so any hex
    // color here recolors the whole frame without needing a new sprite
    // per color scheme.
    public string boxColor = "#1B140FE0";

    // Choice-pill tint. Same white-sprite-plus-tint idea as boxColor,
    // applied to every spawned choice button. Kept separate from
    // boxColor/textColor since choices usually want to read as
    // distinctly "interactive" against the dialogue panel behind them.
    public string choiceColor = "#4A3626E6";

    // Filenames (no extension) of 9-slice sprites under
    // Assets/Resources/UI/. Loaded via Resources.Load<Sprite>("UI/" + name).
    // Leave either one as "" to fall back to a flat, untextured Image
    // (the original look) -- useful before you've imported any sprites,
    // or if you deliberately want the panel bordered but choices flat.
    public string panelBorderSprite = "dialogue_panel_frame";
    public string choiceBorderSprite = "choice_pill_frame";
}
