using UnityEngine;

public class PokemonPreviewModalBootstrap : MonoBehaviour
{
    [SerializeField]
    private PokemonCard cardPrefab;

    private void Awake()
    {
        PokemonPreviewModal.Ensure(cardPrefab);
    }
}
