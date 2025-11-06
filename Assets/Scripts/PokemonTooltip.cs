using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
[RequireComponent(typeof(CanvasGroup))]
public class PokemonTooltip : MonoBehaviour
{
    [Header("Wiring")]
    public TMP_Text nameLabel;
    public Image type1Image;
    public Image type2Image;
    public TMP_Text descriptionText; // <— your notes text
    public CanvasGroup cg;
    public LayoutElement layoutElement; // <— add via Inspector

    [Header("Sizing")]
    public float minWidth = 260f;
    public float maxWidth = 560f;
    public float contentPadding = 40f; // extra breathing room

    public bool IsVisible => cg && cg.alpha > 0.001f;

    public Vector2 PreferredSize
    {
        get
        {
            var rt = (RectTransform)transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            float w = LayoutUtility.GetPreferredWidth(rt);
            float h = LayoutUtility.GetPreferredHeight(rt);
            return new Vector2(w, h);
        }
    }

    void Awake()
    {
        if (!cg)
            cg = GetComponent<CanvasGroup>();
        if (cg)
        {
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;
        }

        var bg = GetComponent<Image>();
        if (bg)
            bg.raycastTarget = false;

        // Hide description by default (Pokémon mode)
        if (descriptionText)
            descriptionText.gameObject.SetActive(false);
    }

    // Normal Pokémon mode (types visible, description hidden)
    public void SetContent(string name, string type1, string type2)
    {
        ApplyPokemonContent(name, type1, type2);
        ApplyWidth(-1); // reset preferred width
    }

    // Update tooltip mode (description shown, left-aligned, types hidden)
    public void SetNotes(string title, string rawNotes)
    {
        if (nameLabel)
            nameLabel.text = title ?? "";

        // Hide type icons in notes mode
        if (type1Image)
        {
            type1Image.enabled = false;
            type1Image.sprite = null;
        }
        if (type2Image)
        {
            type2Image.enabled = false;
            type2Image.sprite = null;
        }

        if (descriptionText)
        {
            descriptionText.gameObject.SetActive(true);
            descriptionText.alignment = TextAlignmentOptions.TopLeft;
            descriptionText.text = FormatNotes(rawNotes);
        }

        // Measure text and choose a good width (clamped)
        float targetWidth =
            MeasureNotesWidth(descriptionText, maxWidth - contentPadding) + contentPadding;
        targetWidth = Mathf.Clamp(targetWidth, minWidth, maxWidth);
        ApplyWidth(targetWidth);
    }

    public void SetVisible(bool visible, bool immediate, float duration = 0.1f)
    {
        if (!cg)
            return;
        StopAllCoroutines();
        if (immediate)
            cg.alpha = visible ? 1f : 0f;
        else
            StartCoroutine(FadeCo(visible ? 1f : 0f, duration));
    }

    System.Collections.IEnumerator FadeCo(float target, float d)
    {
        float start = cg.alpha;
        float t = 0f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0, 1, t / d));
            yield return null;
        }
        cg.alpha = target;
    }

    // ----------------- helpers -----------------

    void ApplyPokemonContent(string name, string type1, string type2)
    {
        if (nameLabel)
            nameLabel.text = name ?? "";

        var s1 = !string.IsNullOrEmpty(type1) ? TypeIconLibrary.Instance.Get(type1) : null;
        var s2 = !string.IsNullOrEmpty(type2) ? TypeIconLibrary.Instance.Get(type2) : null;

        if (type1Image)
        {
            type1Image.sprite = s1;
            type1Image.enabled = s1 != null;
        }
        if (type2Image)
        {
            type2Image.sprite = s2;
            type2Image.enabled = s2 != null;
        }

        if (descriptionText)
            descriptionText.gameObject.SetActive(false);

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }

    private static string FormatNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "";

        // Ensure bullets and line breaks:
        // turn: "- foo - bar" or " - foo" into "\n• foo\n• bar"
        var s = notes.Replace("\r", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*-\s*", "\n• ");
        s = s.Trim();
        if (!s.StartsWith("• "))
            s = "• " + s;
        return s;
    }

    float MeasureNotesWidth(TMP_Text t, float hardMax)
    {
        if (t == null)
            return minWidth;
        // Let TMP tell us the width needed for a single line up to hardMax
        var pref = t.GetPreferredValues(t.text, hardMax, 0);
        return Mathf.Min(pref.x, hardMax);
    }

    void ApplyWidth(float preferred)
    {
        if (!layoutElement)
            return;
        layoutElement.preferredWidth = preferred; // -1 means “use layout”
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }
}
