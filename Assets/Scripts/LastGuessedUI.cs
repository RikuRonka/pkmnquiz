using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LastGuessedUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField]
    private QuizManager quizManager;

    [SerializeField]
    private TMP_Text nameLabel;

    [SerializeField]
    private TMP_Text lastGuessedText;

    [SerializeField]
    private Image spriteImage;

    [SerializeField]
    private Image typeIconL;

    [SerializeField]
    private Image typeIconR;

    private void Awake()
    {
        ClearUI();
    }

    private void OnEnable()
    {
        if (!quizManager)
            quizManager = FindFirstObjectByType<QuizManager>();

        if (quizManager)
            quizManager.OnPokemonSolved += HandleSolved;
    }

    private void OnDisable()
    {
        if (quizManager)
            quizManager.OnPokemonSolved -= HandleSolved;
    }

    private void HandleSolved(Pokemon p)
    {
        if (p == null)
            return;

        if (nameLabel)
            nameLabel.text = p.name;

        lastGuessedText.gameObject.SetActive(true);

        if (spriteImage)
        {
            var spr = SpriteLibrary.Instance?.ByPokemon(p);
            spriteImage.sprite = spr;
            spriteImage.enabled = spr != null;
            spriteImage.preserveAspect = true;
        }

        if (typeIconL || typeIconR)
        {
            var t0 = (p.types != null && p.types.Length > 0) ? p.types[0] : null;
            var t1 = (p.types != null && p.types.Length > 1) ? p.types[1] : null;

            var s0 = !string.IsNullOrEmpty(t0) ? TypeIconLibrary.Instance.Get(t0) : null;
            var s1 = !string.IsNullOrEmpty(t1) ? TypeIconLibrary.Instance.Get(t1) : null;

            if (typeIconL)
            {
                typeIconL.sprite = s0;
                typeIconL.enabled = s0 != null;
            }
            if (typeIconR)
            {
                typeIconR.sprite = s1;
                typeIconR.enabled = s1 != null;
            }
        }
    }

    private void ClearUI()
    {
        if (nameLabel)
            nameLabel.text = string.Empty;

        if (spriteImage)
        {
            spriteImage.sprite = null;
            spriteImage.enabled = false;
        }

        if (typeIconL)
        {
            typeIconL.sprite = null;
            typeIconL.enabled = false;
        }

        if (typeIconR)
        {
            typeIconR.sprite = null;
            typeIconR.enabled = false;
        }
        if (lastGuessedText)
            lastGuessedText.gameObject.SetActive(false);
    }
}
