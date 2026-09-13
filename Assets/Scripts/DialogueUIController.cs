using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ink.Runtime;

// Drives the dialogue UI from an ink Story: one text block for the
// accumulated story text, and a dynamically-built list of choice buttons.
// ApplyLayout additionally lets a DialogueLayoutConfig (loaded from JSON by
// GameBootstrapper) control this UI's position, size, and colors -- so
// none of that needs re-wiring by hand in the Inspector when it changes.
public class DialogueUIController : MonoBehaviour
{
    [Header("UI references (assign in Inspector)")]
    public TMP_Text storyText;
    public RectTransform choiceContainer;
    public Button choiceButtonPrefab;

    [Header("Layout target (assign once)")]
    [Tooltip("The semi-transparent box behind the story text. Its RectTransform IS the text area -- storyText should be a child of this object, stretched to fill it with some padding, so it moves and resizes along with the box automatically.")]
    public Image dialoguePanelBackground;

    private Story _story;
    private readonly List<Button> _spawnedButtons = new List<Button>();

    public void Bind(Story story)
    {
        _story = story;
        Refresh();
    }

    public void ApplyLayout(DialogueLayoutConfig config)
    {
        if (config == null) return;

        if (dialoguePanelBackground != null)
        {
            if (ColorUtility.TryParseHtmlString(config.boxColor, out var boxColor))
            {
                dialoguePanelBackground.color = boxColor;
            }

            var panelRect = dialoguePanelBackground.rectTransform;
            panelRect.anchorMin = config.textAreaAnchorMin.ToVector2();
            panelRect.anchorMax = config.textAreaAnchorMax.ToVector2();
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
        }

        if (storyText != null)
        {
            if (ColorUtility.TryParseHtmlString(config.textColor, out var textColor))
            {
                storyText.color = textColor;
            }
            storyText.fontSize = config.fontSize;
        }

        if (choiceContainer != null)
        {
            choiceContainer.anchorMin = config.choiceAreaAnchorMin.ToVector2();
            choiceContainer.anchorMax = config.choiceAreaAnchorMax.ToVector2();
            choiceContainer.offsetMin = Vector2.zero;
            choiceContainer.offsetMax = Vector2.zero;
        }
    }

    private void Refresh()
    {
        ClearChoiceButtons();

        var sb = new StringBuilder();
        while (_story.canContinue)
        {
            sb.AppendLine(_story.Continue().Trim());
        }
        storyText.text = sb.ToString();

        for (int i = 0; i < _story.currentChoices.Count; i++)
        {
            int choiceIndex = i; // local copy -- otherwise every button's
                                  // listener would capture the same 'i'
            Choice choice = _story.currentChoices[i];

            Button button = Instantiate(choiceButtonPrefab, choiceContainer);
            button.GetComponentInChildren<TMP_Text>().text = choice.text;
            button.onClick.AddListener(() => SelectChoice(choiceIndex));
            _spawnedButtons.Add(button);
        }

        if (_story.currentChoices.Count == 0)
        {
            storyText.text += "\n\n--- END ---";
        }
    }

    private void SelectChoice(int index)
    {
        _story.ChooseChoiceIndex(index);
        Refresh();
    }

    private void ClearChoiceButtons()
    {
        foreach (var button in _spawnedButtons)
        {
            Destroy(button.gameObject);
        }
        _spawnedButtons.Clear();
    }
}
