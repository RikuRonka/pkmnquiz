using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class TypeFilterButton : MonoBehaviour
{
    [Header("Config")]
    [SerializeField]
    string typeName; // "bug", "water", ...

    [Header("UI")]
    [SerializeField]
    Button button;

    [SerializeField]
    TMP_Text label;

    [SerializeField]
    Image icon; // <-- drag your Icon image here in the prefab

    [SerializeField]
    Image icon2; // <-- drag your Icon image here in the prefab

    void Awake()
    {
        Apply(typeName);
        if (button == null)
            button = GetComponent<Button>();
        if (button != null)
            button.onClick.AddListener(() => MenuRouter.PlayTypeQuiz(typeName));
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Keep it updated in the editor when you change typeName
        if (!Application.isPlaying)
            Apply(typeName);
    }
#endif

    public void Apply(string key)
    {
        typeName = key?.Trim().ToLowerInvariant();

        // Title-case label ("Bug", "Water")
        if (label)
        {
            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            label.text = ti.ToTitleCase(typeName ?? string.Empty);
        }

        // Fetch sprite from your icon library
        if (icon)
        {
            Sprite sp = null;

            // Preferred: central icon library (fast, cached)
            // Replace the call below with the method your project exposes.
            // e.g. TypeIconLibrary.Instance.Get("bug")
            if (TypeIconLibrary.Instance != null)
                sp = TypeIconLibrary.Instance.Get(typeName);

            // Fallback: Resources (if you have them under a predictable path)
            if (sp == null)
                sp = Resources.Load<Sprite>($"Sprites/TypeIcons/{typeName}");

            icon.sprite = sp;
            icon.enabled = sp != null;
            if (icon2)
            {
                icon2.sprite = sp;
                icon2.enabled = sp;
            }
        }
    }
}
