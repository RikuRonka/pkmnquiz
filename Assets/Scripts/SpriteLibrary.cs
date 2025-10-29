using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public sealed class SpriteLibrary
{
    private static SpriteLibrary _instance;
    public static SpriteLibrary Instance => _instance ??= new SpriteLibrary();

    // Caches
    private readonly Dictionary<int, Sprite> _byId = new();           // 001 -> Sprite
    private readonly Dictionary<string, Sprite> _byKey = new();       // "bulbasaur" etc. (filled lazily)

    // -------- Public API --------

    /// <summary>
    /// Preload only the sprites you need (by numeric id). Call this with your targetList ids.
    /// </summary>
    public IEnumerator PreloadAsync(IEnumerable<int> ids)
    {
        if (ids == null) yield break;

        // Distinct to avoid duplicate loads
        foreach (var id in ids.Distinct())
        {
            if (_byId.ContainsKey(id)) continue;

            // Most projects store 3-digit zero-padded files; try both to be safe.
            var req = Resources.LoadAsync<Sprite>($"Sprites/{id:000}");
            yield return req;

            var sp = req.asset as Sprite;
            if (!sp)
            {
                // Fallback: non-padded
                req = Resources.LoadAsync<Sprite>($"Sprites/{id}");
                yield return req;
                sp = req.asset as Sprite;
            }

            if (sp) _byId[id] = sp;
        }
    }

    private static Sprite LoadAny(string pathOrKey)
    {
        if (string.IsNullOrWhiteSpace(pathOrKey)) return null;

        // 1) Try direct single-sprite load
        var sp = Resources.Load<Sprite>(pathOrKey);
        if (sp) return sp;

        // 2) Try as a sliced sprite sheet
        var all = Resources.LoadAll<Sprite>(pathOrKey);
        if (all != null && all.Length > 0)
        {
            // choose the best slice: exact file-name match, normalized name match, or first
            var file = System.IO.Path.GetFileName(pathOrKey);
            var norm = GuessNormalizer.Key(file ?? "");
            var pick = all.FirstOrDefault(s => s.name.Equals(file, System.StringComparison.OrdinalIgnoreCase))
                    ?? all.FirstOrDefault(s => GuessNormalizer.Key(s.name) == norm)
                    ?? all[0];
            return pick;
        }

        // 3) Try relative under Sprites/
        var rel = $"Sprites/{pathOrKey.TrimStart('/').Replace("Sprites/", "")}";
        sp = Resources.Load<Sprite>(rel);
        if (sp) return sp;

        all = Resources.LoadAll<Sprite>(rel);
        if (all != null && all.Length > 0) return all[0];

        return null;
    }

    /// <summary>
    /// Get a sprite for a Pokémon. Uses cache if available, otherwise loads on demand.
    /// </summary>
    public Sprite ByPokemon(Pokemon p)
    {
        // 1) id cache
        if (_byId.TryGetValue(p.id, out var s)) return s;

        // 2) try id files
        s = LoadAny($"Sprites/{p.id:000}") ?? LoadAny($"Sprites/{p.id}");
        if (s) return Cache(p, s);

        // 3) explicit path from JSON (now supports sheets)
        if (!string.IsNullOrWhiteSpace(p.sprite))
        {
            s = LoadAny(p.sprite);
            if (s) return Cache(p, s);
        }

        // 4) name keys — works for files like "venusaur_mega" in a sheet
        var norm = GuessNormalizer.Key(p.name);                  // "venusaurmega"
        var lettersOnly = new string(norm.Where(char.IsLetter).ToArray()); // "venusaurmega" (same here)

        s = LoadAny($"Sprites/{norm}") ?? LoadAny($"Sprites/{lettersOnly}")
          ?? LoadAny(norm) ?? LoadAny(lettersOnly);

        if (s) return Cache(p, s);

        if (Helpers.IsMega(p))
        {
            foreach (var cand in MegaCandidatesFor(p))
            {
                // try dictionary (preloaded keys) first
                if (_byKey.TryGetValue(cand.ToLowerInvariant(), out var sMegaDict))
                    return Cache(p, sMegaDict);

                // then try loading from Resources (handles sliced sheets)
                var sMega = LoadAny(cand);
                if (sMega) return Cache(p, sMega);
            }
        }

        Debug.LogWarning($"[SpriteLibrary] Missing sprite for #{p.id} {p.name}. Path hints tried with id, explicit path, and name keys.");
        return null;
    }


    private Sprite Cache(Pokemon p, Sprite s)
    {
        if (s == null) return null;
        _byId[p.id] = s;

        // also cache by normalized key for future lookups
        var norm = GuessNormalizer.Key(p.name);
        if (!string.IsNullOrEmpty(norm) && !_byKey.ContainsKey(norm))
            _byKey[norm] = s;

        return s;
    }

    private static readonly Regex ParensFormRx = new(@"\s*\(.*?\)\s*$", RegexOptions.Compiled);

    private static IEnumerable<string> MegaCandidatesFor(Pokemon p)
    {
        // base name without the "(Mega …)" suffix
        var baseName = ParensFormRx.Replace(p.name ?? "", "").Trim();
        var baseKey = GuessNormalizer.Key(baseName);           // "venusaur"
        var formNorm = GuessNormalizer.Key(p.name ?? "");       // maybe "charizardmegax" etc.

        // common filename patterns seen in packs
        //   <base>_mega
        //   <base>_mega_x / _mega_y
        //   <base>_megax / _megay
        // (also allow paths under Sprites/mega/)
        var list = new List<string>
    {
        $"{baseKey}_mega",
        $"{baseKey}_mega_x",
        $"{baseKey}_mega_y",
        $"{baseKey}_megax",
        $"{baseKey}_megay",
        // with folder hints
        $"Sprites/mega/{baseKey}_mega",
        $"Sprites/mega/{baseKey}_mega_x",
        $"Sprites/mega/{baseKey}_mega_y",
        $"Sprites/mega/{baseKey}_megax",
        $"Sprites/mega/{baseKey}_megay",
    };

        // If the normalized name already ends with "mega[x|y]" without underscore,
        // also try inserting the underscore once.
        if (formNorm.EndsWith("megax"))
            list.Insert(0, $"{baseKey}_mega_x");
        else if (formNorm.EndsWith("megay"))
            list.Insert(0, $"{baseKey}_mega_y");
        else if (formNorm.EndsWith("mega"))
            list.Insert(0, $"{baseKey}_mega");

        return list.Distinct();
    }
}
