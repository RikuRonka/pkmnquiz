using UnityEngine;

public class MenuRouter : MonoBehaviour
{
    public static void PlayFullQuiz() => LoadingManager.Instance.LoadQuiz(0, null);

    public static void PlayGenQuiz(int gen) => LoadingManager.Instance.LoadQuiz(gen, null);

    public static void PlayTypeQuiz(string typeKey) => LoadingManager.Instance.LoadQuiz(0, typeKey);
}
