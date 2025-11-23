using System.Collections.Generic;
using System.Linq;

public static class ExactNameOverrides
{
    private static readonly Dictionary<string, int> Map = new()
    {
        ["mew"] = 151,
        ["mewtwo"] = 150,

        ["porygon"] = 137,
        ["porygon2"] = 233,
        ["porygonz"] = 474,
    };

    public static Pokemon TryGet(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;
        string key = Normalize(rawText);
        if (Map.TryGetValue(key, out int id))
        {
            var p = PokemonDatabase.Instance.All().FirstOrDefault(x => x.id == id);
            return p;
        }
        return null;
    }

    private static string Normalize(string s)
    {
        s = s.Trim().ToLowerInvariant().Replace("é", "e");
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        return sb.ToString();
    }
}
