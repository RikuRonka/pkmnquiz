using TMPro;
using UnityEngine;

public class PatchNotesView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField]
    private TMP_Text notesText;

    [Header("Content")]
    [TextArea(8, 40)]
    public string patchNotes;

    void Awake()
    {
        if (notesText != null)
        {
            var asset = Resources.Load<TextAsset>("PatchNotes");
            if (asset != null && !string.IsNullOrEmpty(asset.text))
                notesText.text = asset.text;
            else
                notesText.text = patchNotes;
        }
    }

    void OnEnable()
    {
        MultiplayerMenuPanel.SetOverlayVisible(false);
        SingleplayerScoreboardPanel.SetOverlayVisible(false);
        SingleplayerProgressResetPanel.SetOverlayVisible(false);
        MainMenuController.SetMenuChromeVisible(false);
        transform.SetAsLastSibling();
    }

    void OnDisable()
    {
        MultiplayerMenuPanel.SetOverlayVisible(true);
        SingleplayerScoreboardPanel.SetOverlayVisible(true);
        SingleplayerProgressResetPanel.SetOverlayVisible(true);
        MainMenuController.SetMenuChromeVisible(true);
    }
}
