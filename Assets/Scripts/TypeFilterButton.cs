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
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
            Apply(typeName);
    }
#endif

    public void Apply(string key)
    {
        typeName = key?.Trim().ToLowerInvariant();

        if (label)
        {
            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            label.text = ti.ToTitleCase(typeName ?? string.Empty);
        }

        if (icon)
        {
            Sprite sp = null;

            if (TypeIconLibrary.Instance != null)
                sp = TypeIconLibrary.Instance.Get(typeName);

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
