// DexOrder.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public static class DexOrder
{
    public class Entry
    {
        public string key;     // normalized name/alias
        public string section; // current section label (may be null)
        public int index;      // order index within the file
    }

    private static readonly Dictionary<string, Entry> _byKey = new();  // key -> entry
    private static readonly List<string> _sectionsInOrder = new();     // for first-appearance
    private static string _firstSection = null;

    public static string FirstSection => _firstSection;
    public static IReadOnlyList<string> Sections => _sectionsInOrder;

    public static void LoadForGeneration(int gen)
    {
        _byKey.Clear();
        _sectionsInOrder.Clear();
        _firstSection = null;

        var ta = Resources.Load<TextAsset>($"Data/dexorder_gen{gen}");
        if (!ta) return;

        string currentSection = null;
        int i = 0;

        foreach (var raw in ta.text.Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith('#')) continue;

            if (line.StartsWith("## "))
            {
                currentSection = line.Substring(3).Trim();
                if (string.IsNullOrEmpty(_firstSection)) _firstSection = currentSection;
                _sectionsInOrder.Add(currentSection);
                continue;
            }

            var key = GuessNormalizer.Key(line);
            if (_byKey.ContainsKey(key)) continue;

            _byKey[key] = new Entry { key = key, section = currentSection, index = i++ };
        }
    }

    public static int GetIndex(Pokemon p)
    {
        // match by name first
        if (TryGetEntry(p.name, out var e)) return e.index;

        // then aliases
        if (p.aliases != null)
            foreach (var a in p.aliases)
                if (TryGetEntry(a, out e)) return e.index;

        // fallback: family-based stable order
        var baseId = p.baseId != 0 ? p.baseId : p.id;
        var formBias = string.IsNullOrEmpty(p.formKey) ? 0 : 1;
        return baseId * 10 + formBias;
    }

    public static string GetSection(Pokemon p, int gen)
    {
        if (p == null) return string.Empty;

        return gen switch
        {
            6 => Helpers.IsMega(p) ? "Mega Evolutions" : "Kalos (Gen 6)",

            8 => Helpers.IsGmax(p) ? "Gigantamax"
                 : Helpers.IsHisui(p) ? "Hisui Forms"
                 : "Galar (Gen 8)",

            9 => /* if you add paradox tagging later */ "Paldea (Gen 9)",

            _ => Helpers.GetGenTitle(gen),
        };
    }

    private static bool TryGetEntry(string rawName, out Entry e)
    {
        var key = GuessNormalizer.Key(rawName);
        return _byKey.TryGetValue(key, out e);
    }

    public static int GetSectionOrder(string name, int gen)
    {
        // Gen 6: base first, megas after
        if (gen == 6)
        {
            if (string.Equals(name, "Kalos (Gen 6)")) return 0;
            if (string.Equals(name, "Mega Evolutions")) return 1;
        }

        // Gen 8 example (if you use these)
        if (gen == 8)
        {
            if (string.Equals(name, "Galar (Gen 8)")) return 0;
            if (string.Equals(name, "Gigantamax")) return 1;
            if (string.Equals(name, "Hisui")) return 2;
        }

        // Gen 9 example
        if (gen == 9)
        {
            if (string.Equals(name, "Paldea (Gen 9)")) return 0;
            if (string.Equals(name, "Kitakami")) return 1;
            if (string.Equals(name, "Blueberry")) return 2;
        }

        return 0; // default
    }
}
