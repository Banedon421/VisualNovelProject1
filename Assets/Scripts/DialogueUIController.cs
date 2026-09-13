using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ink.Runtime;

// Drives the dialogue UI from an ink Story: one text block for the
// accumulated story text, and a dynamically-built list of choice buttons
// (built and destroyed as the player moves through the story). Build the
// UI hierarchy in the Editor (see setup notes) and assign the references
// below in the Inspector -- this script doesn't create the UI itself, it
// only fills in and reacts to UI that already exists in the scene.
public class DialogueUIController : MonoBehaviour
{
    [Header("UI references (assign in Inspector)")]
    public TMP_Text storyText;
    public RectTransform choiceContainer;
    public Button choiceButtonPrefab;

    private Story _story;
    private readonly List<Button> _spawnedButtons = new List<Button>();

    public void Bind(Story story)
    {
        _story = story;
        Refresh();
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
