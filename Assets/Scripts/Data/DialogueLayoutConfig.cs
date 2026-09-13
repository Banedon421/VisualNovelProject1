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
    public string boxColor = "#00000099";
}
