using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TypeFilterButton : MonoBehaviour
{
    public string typeName;
    public Button button;

    void Awake()
    {
        if (!button)
            button = GetComponent<Button>();
        if (button)
            button.onClick.AddListener(OnClicked);
    }

    void OnClicked()
    {
        GameSettings.Generation = 0;
        GameSettings.TypeFilter = string.IsNullOrWhiteSpace(typeName) ? null : new[] { typeName };

        SceneManager.LoadScene("Quiz");
    }
}
