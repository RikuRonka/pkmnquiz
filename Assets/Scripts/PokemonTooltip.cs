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
    public CanvasGroup cg;

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
        {
            bg.raycastTarget = false;
        }
    }

    public void SetContent(string name, string type1, string type2)
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

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }

    public void SetVisible(bool visible, bool immediate, float duration = 0.1f)
    {
        if (!cg)
            return;
        StopAllCoroutines();
        if (immediate)
        {
            cg.alpha = visible ? 1f : 0f;
        }
        else
        {
            StartCoroutine(FadeCo(visible ? 1f : 0f, duration));
        }
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
}
