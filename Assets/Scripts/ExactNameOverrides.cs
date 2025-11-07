using System.Collections.Generic;
using System.Linq;

public static class ExactNameOverrides
{
    // Normalized keys (letters+digits only, lowercase; same as your KeyKeepDigits behavior)
    private static readonly Dictionary<string, int> Map = new()
    {
        // MEW family
        ["mew"] = 151,
        ["mewtwo"] = 150,

        // PORYGON family
        ["porygon"] = 137,
        ["porygon2"] = 233,
        ["porygonz"] = 474, // also matches "porygon-z" after normalization
        // Add more here if you find other problem cases:
        // ["mrmime"] = <id>, ["mimejr"] = <id>, etc.
    };

    // 1) Try find by exact normalized key -> Pokemon
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

    // Use same normalization style as your KeyKeepDigits: keep letters+digits only, lowercase, 'é' -> 'e'
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
