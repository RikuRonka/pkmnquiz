using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ConfirmDialog : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button cancelBtn;

    private Action onConfirm;
    private Action onCancel;
    private bool showing;
    public bool IsShowing => showing;

    void Awake()
    {
        if (!group) group = GetComponent<CanvasGroup>();
        HideImmediate();

        if (confirmBtn) confirmBtn.onClick.AddListener(() => { Confirm(); });
        if (cancelBtn) cancelBtn.onClick.AddListener(() => { Cancel(); });
    }

    public void Show(string title, string message, string confirmLabel, string cancelLabel, Action confirmAction, Action cancelAction = null)
    {
        titleText.text = title ?? "Confirm";
        messageText.text = message ?? "";
        confirmBtn.GetComponentInChildren<TMP_Text>().text = string.IsNullOrEmpty(confirmLabel) ? "OK" : confirmLabel;
        cancelBtn.GetComponentInChildren<TMP_Text>().text = string.IsNullOrEmpty(cancelLabel) ? "Cancel" : cancelLabel;

        onConfirm = confirmAction;
        onCancel = cancelAction;

        gameObject.SetActive(true);
        group.alpha = 1f;
        group.blocksRaycasts = true;
        group.interactable = true;
        showing = true;

        // Optional: focus confirm button for keyboard/Enter
        confirmBtn.Select();
    }

    public void Hide()
    {
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        gameObject.SetActive(false);
        showing = false;
    }

    private void HideImmediate() => Hide();

    private void Confirm()
    {
        if (!showing) return;
        var cb = onConfirm;
        Hide();
        cb?.Invoke();
    }

    private void Cancel()
    {
        if (!showing) return;
        var cb = onCancel;
        Hide();
        cb?.Invoke();
    }

    void Update()
    {
        if (!showing) return;

        // New Input System
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame) Cancel();
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Confirm();
        }

        // Legacy Input Manager
#else
    if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Cancel();
    if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) Confirm();
#endif
    }
}
