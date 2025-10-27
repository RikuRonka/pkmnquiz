using UnityEngine;

public class SectionGroup : MonoBehaviour
{
    public SectionHeader header;
    public Transform gridRoot;
    public int CardCount => gridRoot.childCount;
    public float HeaderHeight => header && header.gameObject.activeInHierarchy
        ? header.GetComponent<RectTransform>().rect.height
        : 0f;

    public void SetTitle(string title)
    {
        if (!header) return;

        // Always keep the header enabled; show empty string if none.
        header.gameObject.SetActive(true);
        header.Set(string.IsNullOrEmpty(title) ? "" : title);
    }
}