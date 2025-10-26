using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    // Called by the Gen buttons
    public void PlayGen(int gen)
    {
        GameSettings.Generation = gen;
        GameSettings.TypeFilter = null;
        SceneManager.LoadScene("Quiz"); // <- make sure the scene name matches
    }

    // Optional: type-only quiz (across all gens or a default gen)
    public void PlayType(string typeName)
    {
        GameSettings.Generation = null;                // keep quiz scene’s default gen or handle all gens
        GameSettings.TypeFilter = new[] { typeName };  // e.g., "Fire"
        SceneManager.LoadScene("Quiz");
    }
}
