using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    public void PlayGen(int gen)
    {
        GameSettings.Generation = gen;
        GameSettings.TypeFilter = null;
        SceneManager.LoadScene("Quiz");
    }

    public void PlayType(string typeName)
    {
        GameSettings.Generation = null;
        GameSettings.TypeFilter = new[] { typeName };
        SceneManager.LoadScene("Quiz");
    }
}
