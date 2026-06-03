using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        if (loaded)
            return;
        var json = Resources.Load<TextAsset>("Data/pokemon");
        if (json == null)
        {
            Debug.LogError("Missing Resources/Data/pokemon.json");
            return;
        }
        var list = JsonUtility.FromJson<PokemonList>(json.text);
        all = list.pokemon.ToList();
        ApplyEvolutionPaths(json.text, all);

        byId.Clear();
        byKey.Clear();

        foreach (var p in all)
        {
            byId[p.id] = p;
            var keys = new HashSet<string> { GuessNormalizer.Key(p.name) };
            if (p.aliases != null)
                foreach (var a in p.aliases)
                    keys.Add(GuessNormalizer.Key(a));

            if (
                p.name.StartsWith("nidoran ", System.StringComparison.InvariantCultureIgnoreCase)
                || p.name.StartsWith("Nidoran")
            )
            {
                keys.Add(GuessNormalizer.Key(p.name.Replace("♀", "f")));
                keys.Add(GuessNormalizer.Key(p.name.Replace("♂", "m")));
            }

            foreach (var k in keys)
            {
                if (!byKey.ContainsKey(k))
                    byKey[k] = p;
            }
        }
        loaded = true;
    }

    public IReadOnlyList<Pokemon> All()
    {
        LoadIfNeeded();
        return all;
    }

    public Pokemon FindByGuess(string guess)
    {
        LoadIfNeeded();
        var key = GuessNormalizer.Key(guess);
        byKey.TryGetValue(key, out var p);
        return p;
    }

    private static void ApplyEvolutionPaths(string json, IReadOnlyList<Pokemon> pokemon)
    {
        if (string.IsNullOrWhiteSpace(json) || pokemon == null || pokemon.Count == 0)
            return;

        var byPokemonId = pokemon.ToDictionary(p => p.id);
        foreach (string pokemonJson in EnumeratePokemonObjects(json))
        {
            if (!TryReadIntProperty(pokemonJson, "id", out int id))
                continue;

            if (
                !byPokemonId.TryGetValue(id, out var p)
                || p == null
                || p.evolution == null
            )
                continue;

            var paths = ParseEvolutionPaths(pokemonJson);
            if (paths.Count > 0)
                p.evolution.paths = paths.ToArray();
        }
    }

    private static IEnumerable<string> EnumeratePokemonObjects(string json)
    {
        int key = json.IndexOf("\"pokemon\"", System.StringComparison.Ordinal);
        if (key < 0)
            yield break;

        int arrayStart = json.IndexOf('[', key);
        if (arrayStart < 0)
            yield break;

        bool inString = false;
        bool escaped = false;
        int depth = 0;
        int objectStart = -1;

        for (int i = arrayStart + 1; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                    objectStart = i;
                depth++;
                continue;
            }

            if (c == '}')
            {
                depth--;
                if (depth == 0 && objectStart >= 0)
                {
                    yield return json.Substring(objectStart, i - objectStart + 1);
                    objectStart = -1;
                }
                continue;
            }

            if (c == ']' && depth == 0)
                yield break;
        }
    }

    private static bool TryReadIntProperty(string json, string propertyName, out int value)
    {
        value = 0;
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*(\\d+)"
        );

        return match.Success && int.TryParse(match.Groups[1].Value, out value);
    }

    private static List<string[]> ParseEvolutionPaths(string pokemonJson)
    {
        var result = new List<string[]>();
        int key = pokemonJson.IndexOf("\"paths\"", System.StringComparison.Ordinal);
        if (key < 0)
            return result;

        int arrayStart = pokemonJson.IndexOf('[', key);
        if (arrayStart < 0)
            return result;

        if (!TryExtractBalanced(pokemonJson, arrayStart, '[', ']', out string pathsJson))
            return result;

        bool inString = false;
        bool escaped = false;
        int depth = 0;
        int innerStart = -1;

        for (int i = 0; i < pathsJson.Length; i++)
        {
            char c = pathsJson[i];

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '[')
            {
                if (depth == 1)
                    innerStart = i;
                depth++;
                continue;
            }

            if (c == ']')
            {
                if (depth == 2 && innerStart >= 0)
                {
                    string innerJson = pathsJson.Substring(innerStart, i - innerStart + 1);
                    var names = ParseStringArray(innerJson);
                    if (names.Count > 0)
                        result.Add(names.ToArray());
                    innerStart = -1;
                }

                depth--;
            }
        }

        return result;
    }

    private static bool TryExtractBalanced(
        string source,
        int start,
        char open,
        char close,
        out string value
    )
    {
        value = null;
        bool inString = false;
        bool escaped = false;
        int depth = 0;

        for (int i = start; i < source.Length; i++)
        {
            char c = source[i];

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == open)
                depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    value = source.Substring(start, i - start + 1);
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string> ParseStringArray(string json)
    {
        var result = new List<string>();
        for (int i = 0; i < json.Length; i++)
        {
            if (json[i] != '"')
                continue;

            int start = i + 1;
            bool escaped = false;
            for (i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    result.Add(UnescapeJsonString(json.Substring(start, i - start)));
                    break;
                }
            }
        }

        return result;
    }

    private static string UnescapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                sb.Append(c);
                continue;
            }

            char escaped = value[++i];
            switch (escaped)
            {
                case '"':
                case '\\':
                case '/':
                    sb.Append(escaped);
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u':
                    if (
                        i + 4 < value.Length
                        && int.TryParse(
                            value.Substring(i + 1, 4),
                            System.Globalization.NumberStyles.HexNumber,
                            null,
                            out int code
                        )
                    )
                    {
                        sb.Append((char)code);
                        i += 4;
                    }
                    break;
                default:
                    sb.Append(escaped);
                    break;
            }
        }

        return sb.ToString();
    }
}
