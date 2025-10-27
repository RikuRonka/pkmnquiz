using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public sealed class PokemonDatabase
{
    private static PokemonDatabase _instance;
    public static PokemonDatabase Instance => _instance ??= new PokemonDatabase();


    private readonly Dictionary<int, Pokemon> byId = new();
    private readonly Dictionary<string, Pokemon> byKey = new();
    private List<Pokemon> all = new();


    private bool loaded;


    public void LoadIfNeeded()
    {
        if (loaded) return;
        var json = Resources.Load<TextAsset>("Data/pokemon");
        if (json == null)
        {
            Debug.LogError("Missing Resources/Data/pokemon.json");
            return;
        }
        var list = JsonUtility.FromJson<PokemonList>(json.text);
        all = list.pokemon.ToList();


        byId.Clear();
        byKey.Clear();


        foreach (var p in all)
        {
            byId[p.id] = p;
            var keys = new HashSet<string> { GuessNormalizer.Key(p.name) };
            if (p.aliases != null)
                foreach (var a in p.aliases)
                    keys.Add(GuessNormalizer.Key(a));


            // Special-case common variants
            if (p.name.StartsWith("nidoran ", System.StringComparison.InvariantCultureIgnoreCase) || p.name.StartsWith("Nidoran"))
            {
                // Accept female/male text variants
                keys.Add(GuessNormalizer.Key(p.name.Replace("♀", "f")));
                keys.Add(GuessNormalizer.Key(p.name.Replace("♂", "m")));
            }


            foreach (var k in keys)
            {
                if (!byKey.ContainsKey(k)) byKey[k] = p;
            }
        }
        loaded = true;
    }


    public IReadOnlyList<Pokemon> All() { LoadIfNeeded(); return all; }


    public Pokemon FindByGuess(string guess)
    {
        LoadIfNeeded();
        var key = GuessNormalizer.Key(guess);
        byKey.TryGetValue(key, out var p);
        return p;
    }
}