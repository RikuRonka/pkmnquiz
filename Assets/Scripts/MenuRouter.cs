using UnityEngine;

public class MenuRouter : MonoBehaviour
{
    public void PlayFullQuiz() => LoadingManager.Instance.LoadQuiz(0, null);

    public void PlayGen(int gen) => LoadingManager.Instance.LoadQuiz(gen, null);

    public void PlayType(string typeKey) => LoadingManager.Instance.LoadQuiz(0, typeKey);
}
