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
            notesText.text = patchNotes;
    }

    void OnEnable()
    {
        transform.SetAsLastSibling();
        MultiplayerMenuPanel.SetOverlayVisible(false);
        QuizMultiplayerChatOverlay.SetOverlayVisible(false);
    }

    void OnDisable()
    {
        MultiplayerMenuPanel.SetOverlayVisible(true);
        QuizMultiplayerChatOverlay.SetOverlayVisible(true);
    }
}
