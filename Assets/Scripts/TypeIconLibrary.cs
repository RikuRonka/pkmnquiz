using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed class TypeIconLibrary
{
    private static TypeIconLibrary _i;
    public static TypeIconLibrary Instance => _i ??= new TypeIconLibrary();

    private readonly Dictionary<string, Sprite> map = new();
    private bool loaded;

    public void Preload()
    {
        if (loaded)
            return;

        var sprites = Resources.LoadAll<Sprite>("TypeIcons");
        var sb = new StringBuilder();
        int n = sprites?.Length ?? 0;
        for (int i = 0; i < n; i++)
        {
            var s = sprites[i];
            if (s == null)
                continue;
            var key = s.name.Trim().ToLowerInvariant();
            if (!map.ContainsKey(key))
                map[key] = s;
            sb.Append(key).Append(i == n - 1 ? "" : ", ");
        }

        loaded = true;
    }

    public Sprite Get(string typeName)
    {
        if (!loaded)
            Preload();
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var k = typeName.Trim().ToLowerInvariant();
        map.TryGetValue(k, out var s);
        if (!s)
        {
            Debug.LogWarning(
                $"[TypeIconLibrary] No icon for type '{k}'. Add '{k}.png' under Resources/TypeIcons/"
            );
        }
        return s;
    }
}
