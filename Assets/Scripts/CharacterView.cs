using UnityEngine;
using UnityEngine.UI;

// Attached automatically to every character portrait the SceneBuilder
// instantiates. It doesn't do anything yet -- it exists so that FUTURE
// logic (e.g. reading a "#speaker:dupont" tag off the current ink line)
// can find "the character currently on screen with this id" and act on it
// -- swap portraitImage.sprite for a mood change, move its RectTransform,
// place a name label near it, etc. -- without having to re-derive any of
// this from the scene JSON again at that point.
public class CharacterView : MonoBehaviour
{
    public string characterId;
    public string displayName;
    public Image portraitImage;
}
