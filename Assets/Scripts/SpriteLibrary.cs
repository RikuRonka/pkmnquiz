using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public sealed class SpriteLibrary
{
    private static SpriteLibrary _instance;
    public static SpriteLibrary Instance => _instance ??= new SpriteLibrary();

    private readonly Dictionary<string, Sprite> byKey = new();
    private bool loaded;
    private int countLoaded;

    // matches "_0", "-1", " 2", "(3)" at the end of a name
    private static readonly Regex SuffixNumberRx = new(@"[\s_\-\(\)]\d+$", RegexOptions.Compiled);

    public void Preload()
    {
        if (loaded) return;

        var sprites = Resources.LoadAll<Sprite>("Sprites"); // Assets/Resources/Sprites/*
        foreach (var s in sprites)
        {
            // raw
            var raw = s.name.ToLowerInvariant();
            AddKey(raw, s);

            // normalized (letters+digits, gender symbols -> f/m)
            var norm = GuessNormalizer.Key(s.name);
            if (!string.IsNullOrEmpty(norm)) AddKey(norm, s);

            // base name without common numeric suffixes (bulbasaur_0 -> bulbasaur)
            var trimmed = SuffixNumberRx.Replace(raw, "");
            if (!string.IsNullOrEmpty(trimmed)) AddKey(trimmed, s);
            var trimmedNorm = GuessNormalizer.Key(trimmed);
            if (!string.IsNullOrEmpty(trimmedNorm)) AddKey(trimmedNorm, s);

            // letters-only key (drops digits entirely: "bulbasaur0" -> "bulbasaur")
            var lettersOnly = new string(norm.Where(char.IsLetter).ToArray());
            if (!string.IsNullOrEmpty(lettersOnly)) AddKey(lettersOnly, s);

            // if the name itself is a number, also map zero-padded ID
            if (int.TryParse(raw, out var num))
            {
                var idKey = num.ToString("000");
                AddKey(idKey, s);
            }
        }
        countLoaded = sprites?.Length ?? 0;
        loaded = true;
    }

    private void AddKey(string key, Sprite s)
    {
        if (!byKey.ContainsKey(key)) byKey[key] = s;
    }

    public Sprite ByPokemon(Pokemon p)
    {
        Preload();

        // 1) ID
        var idKey = p.id.ToString("000").ToLowerInvariant();
        if (byKey.TryGetValue(idKey, out var sId)) return sId;

        // 2) explicit path from JSON
        if (!string.IsNullOrEmpty(p.sprite))
        {
            var sByPath = Resources.Load<Sprite>(p.sprite);
            if (sByPath) return sByPath;

            var trimmed = p.sprite.ToLowerInvariant().Replace("sprites/", "");
            if (byKey.TryGetValue(trimmed, out var sTrim)) return sTrim;
            var trimmedNorm = GuessNormalizer.Key(trimmed);
            if (byKey.TryGetValue(trimmedNorm, out var sTrimNorm)) return sTrimNorm;
        }

        // 3) name variants
        var nameNorm = GuessNormalizer.Key(p.name);               // e.g., "bulbasaur", "mrmime", "nidoranf"
        if (byKey.TryGetValue(nameNorm, out var sName)) return sName;

        var lettersOnly = new string(nameNorm.Where(char.IsLetter).ToArray()); // "bulbasaur"
        if (byKey.TryGetValue(lettersOnly, out var sLetters)) return sLetters;

        Debug.LogWarning($"[SpriteLibrary] MISSING sprite for #{p.id} {p.name}. " +
                         $"Tried keys: '{idKey}', '{p.sprite}', '{nameNorm}'. Loaded={countLoaded}. " +
                         $"Ensure a matching file (e.g., 001.png, 'bulbasaur.png', or a sub-sprite like 'bulbasaur_0') exists in Assets/Resources/Sprites.");
        return null;
    }
}
