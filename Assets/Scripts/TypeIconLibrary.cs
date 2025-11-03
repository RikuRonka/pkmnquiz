using System.Collections.Generic;
using UnityEngine;

public sealed class TypeIconLibrary
{
    private static TypeIconLibrary _i;
    public static TypeIconLibrary Instance => _i ??= new TypeIconLibrary();

    private readonly Dictionary<string, Sprite> map = new();
    private readonly HashSet<string> warned = new();
    private bool loaded;

    static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;
        s = s.Trim().ToLowerInvariant();
        // tolerate names like "ice-cream" or "ice cream" -> "icecream"
        s = s.Replace("-", "").Replace(" ", "");
        return s;
    }

    public void Preload()
    {
        if (loaded)
            return;

        var sprites = Resources.LoadAll<Sprite>("TypeIcons");
        int n = sprites?.Length ?? 0;
        for (int i = 0; i < n; i++)
        {
            var s = sprites[i];
            if (!s)
                continue;
            var key = Normalize(s.name);
            if (!map.ContainsKey(key))
                map[key] = s;
        }

        loaded = true;
    }

    public Sprite Get(string typeName)
    {
        if (!loaded)
            Preload();
        var k = Normalize(typeName);
        if (string.IsNullOrEmpty(k))
            return null;

        if (map.TryGetValue(k, out var s) && s)
            return s;

        if (!warned.Contains(k))
        {
            warned.Add(k);
            Debug.LogWarning(
                $"[TypeIconLibrary] No icon for type '{k}'. "
                    + "Add a sprite named '"
                    + k
                    + ".png' under Resources/TypeIcons/"
            );
        }
        return null;
    }

    public Sprite[] GetMany(params string[] typeNames)
    {
        if (typeNames == null || typeNames.Length == 0)
            return System.Array.Empty<Sprite>();
        var list = new List<Sprite>(typeNames.Length);
        foreach (var t in typeNames)
        {
            var sp = Get(t);
            if (sp)
                list.Add(sp);
        }
        return list.ToArray();
    }
}
