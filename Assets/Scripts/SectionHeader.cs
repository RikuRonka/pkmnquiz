using TMPro;
using UnityEngine;

public class SectionHeader : MonoBehaviour
{
    public TMP_Text label;
    public void Set(string text)
    {
        if (label) label.text = text ?? "";
    }
}
