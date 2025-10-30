using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public sealed class SpriteLibrary
{
    private static SpriteLibrary _instance;
    public static SpriteLibrary Instance => _instance ??= new SpriteLibrary();

    private readonly Dictionary<int, Sprite> _byId = new();
    private readonly Dictionary<string, Sprite> _byKey = new();

    public IEnumerator PreloadAsync(IEnumerable<int> ids)
    {
        if (ids == null)
            yield break;

        foreach (var id in ids.Distinct())
        {
            if (_byId.ContainsKey(id))
                continue;

            var req = Resources.LoadAsync<Sprite>($"Sprites/{id:000}");
            yield return req;

            var sp = req.asset as Sprite;
            if (!sp)
            {
                req = Resources.LoadAsync<Sprite>($"Sprites/{id}");
                yield return req;
                sp = req.asset as Sprite;
            }

            if (sp)
                _byId[id] = sp;
        }
    }

    private static Sprite LoadAny(string pathOrKey)
    {
        if (string.IsNullOrWhiteSpace(pathOrKey))
            return null;

        var sp = Resources.Load<Sprite>(pathOrKey);
        if (sp)
            return sp;

        var all = Resources.LoadAll<Sprite>(pathOrKey);
        if (all != null && all.Length > 0)
        {
            var file = System.IO.Path.GetFileName(pathOrKey);
            var norm = GuessNormalizer.Key(file ?? "");
            var pick =
                all.FirstOrDefault(s =>
                    s.name.Equals(file, System.StringComparison.OrdinalIgnoreCase)
                )
                ?? all.FirstOrDefault(s => GuessNormalizer.Key(s.name) == norm)
                ?? all[0];
            return pick;
        }

        var rel = $"Sprites/{pathOrKey.TrimStart('/').Replace("Sprites/", "")}";
        sp = Resources.Load<Sprite>(rel);
        if (sp)
            return sp;

        all = Resources.LoadAll<Sprite>(rel);
        if (all != null && all.Length > 0)
            return all[0];

        return null;
    }

    public Sprite ByPokemon(Pokemon p)
    {
        if (_byId.TryGetValue(p.id, out var s))
            return s;

        s = LoadAny($"Sprites/{p.id:000}") ?? LoadAny($"Sprites/{p.id}");
        if (s)
            return Cache(p, s);

        if (!string.IsNullOrWhiteSpace(p.sprite))
        {
            s = LoadAny(p.sprite);
            if (s)
                return Cache(p, s);
        }

        var norm = GuessNormalizer.Key(p.name);
        var lettersOnly = new string(norm.Where(char.IsLetter).ToArray());

        s =
            LoadAny($"Sprites/{norm}")
            ?? LoadAny($"Sprites/{lettersOnly}")
            ?? LoadAny(norm)
            ?? LoadAny(lettersOnly);

        if (s)
            return Cache(p, s);

        if (Helpers.IsMega(p))
        {
            foreach (var cand in MegaCandidatesFor(p))
            {
                if (_byKey.TryGetValue(cand.ToLowerInvariant(), out var sMegaDict))
                    return Cache(p, sMegaDict);

                var sMega = LoadAny(cand);
                if (sMega)
                    return Cache(p, sMega);
            }
        }

        Debug.LogWarning(
            $"[SpriteLibrary] Missing sprite for #{p.id} {p.name}. Path hints tried with id, explicit path, and name keys."
        );
        return null;
    }

    private Sprite Cache(Pokemon p, Sprite s)
    {
        if (s == null)
            return null;
        _byId[p.id] = s;

        var norm = GuessNormalizer.Key(p.name);
        if (!string.IsNullOrEmpty(norm) && !_byKey.ContainsKey(norm))
            _byKey[norm] = s;

        return s;
    }

    private static readonly Regex ParensFormRx = new(@"\s*\(.*?\)\s*$", RegexOptions.Compiled);

    private static IEnumerable<string> MegaCandidatesFor(Pokemon p)
    {
        var baseName = ParensFormRx.Replace(p.name ?? "", "").Trim();
        var baseKey = GuessNormalizer.Key(baseName);
        var formNorm = GuessNormalizer.Key(p.name ?? "");
        var list = new List<string>
        {
            $"{baseKey}_mega",
            $"{baseKey}_mega_x",
            $"{baseKey}_mega_y",
            $"{baseKey}_megax",
            $"{baseKey}_megay",
            $"Sprites/mega/{baseKey}_mega",
            $"Sprites/mega/{baseKey}_mega_x",
            $"Sprites/mega/{baseKey}_mega_y",
            $"Sprites/mega/{baseKey}_megax",
            $"Sprites/mega/{baseKey}_megay",
        };

        if (formNorm.EndsWith("megax"))
            list.Insert(0, $"{baseKey}_mega_x");
        else if (formNorm.EndsWith("megay"))
            list.Insert(0, $"{baseKey}_mega_y");
        else if (formNorm.EndsWith("mega"))
            list.Insert(0, $"{baseKey}_mega");

        return list.Distinct();
    }
}
