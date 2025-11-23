using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
[RequireComponent(typeof(CanvasGroup))]
public class PokemonTooltip : MonoBehaviour
{
    [Header("Wiring")]
    private TMP_Text nameLabel;

    [SerializeField]
    private Image type1Image;

    [SerializeField]
    private Image type2Image;

    [SerializeField]
    private TMP_Text descriptionText;

    [SerializeField]
    private CanvasGroup cg;

    [SerializeField]
    private LayoutElement layoutElement;

    [Header("Sizing")]
    [SerializeField]
    private float minWidth = 260f;

    [SerializeField]
    private float maxWidth = 800f;

    [SerializeField]
    private float contentPadding = 40f;

    public bool IsVisible => cg && cg.alpha > 0.001f;

    public float pokemonMaxWidth = 520f;

    [SerializeField]
    private RectTransform typesRow;

    [SerializeField]
    private VerticalLayoutGroup vlg;

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
        AutoWire();
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

        if (descriptionText)
            descriptionText.gameObject.SetActive(false);
    }

    float GetMaxScreenTooltipWidth()
    {
        var c = GetComponentInParent<Canvas>();
        if (c == null)
            return maxWidth;

        var rt = c.transform as RectTransform;
        if (rt == null)
            return maxWidth;

        return Mathf.Max(200f, rt.rect.width - 50f);
    }

    float MeasureWideNotes(TMP_Text t)
    {
        if (t == null)
            return minWidth;

        float screenLimit = GetMaxScreenTooltipWidth();
        var pref = t.GetPreferredValues(t.text, screenLimit, 0);

        return Mathf.Min(pref.x, screenLimit);
    }

    void AutoWire()
    {
        if (!nameLabel)
            nameLabel = GetComponentInChildren<TMP_Text>(true);

        var nrt = (RectTransform)nameLabel.transform;
        nrt.anchorMin = new Vector2(0f, nrt.anchorMin.y);
        nrt.anchorMax = new Vector2(1f, nrt.anchorMax.y);
        nrt.offsetMin = new Vector2(0f, nrt.offsetMin.y);
        nrt.offsetMax = new Vector2(0f, nrt.offsetMax.y);
        nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        nameLabel.overflowMode = TextOverflowModes.Overflow;
        nameLabel.alignment = TextAlignmentOptions.Center;

        if (!type1Image || !type2Image)
        {
            var imgs = GetComponentsInChildren<Image>(true);

            foreach (var img in imgs)
            {
                if (img == GetComponent<Image>())
                    continue;
                if (!type1Image)
                {
                    type1Image = img;
                    continue;
                }
                if (!type2Image)
                {
                    type2Image = img;
                    break;
                }
            }
        }
        if (!descriptionText)
        {
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            {
                if (t != nameLabel)
                {
                    descriptionText = t;
                    break;
                }
            }
        }

        if (!cg)
            cg = GetComponent<CanvasGroup>();
    }

    public void SetContent(string name, string type1, string type2)
    {
        ApplyPokemonContent(name, type1, type2);

        LayoutRebuilder.ForceRebuildLayoutImmediate(nameLabel.rectTransform);
        if (typesRow)
            LayoutRebuilder.ForceRebuildLayoutImmediate(typesRow);

        float titleW = nameLabel.GetPreferredValues(nameLabel.text, pokemonMaxWidth, 0f).x;
        float iconsW = typesRow ? LayoutUtility.GetPreferredWidth(typesRow) : 0f;

        int lp = vlg ? vlg.padding.left : 0;
        int rp = vlg ? vlg.padding.right : 0;
        float needed = Mathf.Max(titleW, iconsW) + lp + rp + contentPadding;

        float w = Mathf.Clamp(needed, minWidth, pokemonMaxWidth);
        ApplyWidth(w);
    }

    public void SetNotes(string title, string rawNotes)
    {
        if (nameLabel)
            nameLabel.text = title ?? "";

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
            descriptionText.textWrappingMode = TextWrappingModes.Normal;
            descriptionText.text = FormatNotes(rawNotes);
        }

        float contentW = MeasureWideNotes(descriptionText);
        float targetWidth = Mathf.Clamp(
            contentW + contentPadding,
            minWidth,
            GetMaxScreenTooltipWidth()
        );
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

        var s = notes.Replace("\r", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*-\s*", "\n• ");
        s = s.Trim();
        if (!s.StartsWith("• "))
            s = "• " + s;
        return s;
    }

    void ApplyWidth(float preferred)
    {
        if (!layoutElement)
            return;

        layoutElement.preferredWidth = preferred;
        layoutElement.minWidth = Mathf.Min(preferred, maxWidth);

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }
}
