using System;
using System.Collections.Generic;

public static class Helpers
{
    public static bool HasForm(Pokemon p, string key) =>
        p != null
        && !string.IsNullOrEmpty(p.formKey)
        && p.formKey.Equals(key, StringComparison.OrdinalIgnoreCase);

    public static bool IsHyperspaceMega(Pokemon p) =>
        p != null
        && !string.IsNullOrEmpty(p.formKey)
        && p.formKey.Equals("mega_hyperspace", StringComparison.OrdinalIgnoreCase);

    public static bool NameHas(Pokemon p, string needle) =>
        p != null
        && !string.IsNullOrEmpty(p.name)
        && p.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool IsMega(Pokemon p) =>
        HasForm(p, "mega") || HasForm(p, "megax") || HasForm(p, "megay") || NameHas(p, "Mega ");

    public static bool IsGmax(Pokemon p) =>
        HasForm(p, "gmax") || HasForm(p, "gigantamax") || NameHas(p, "Gigantamax");

    public static bool IsHisui(Pokemon p)
    {
        if (p == null)
            return false;

        var baseId = p.baseId != 0 ? p.baseId : p.id;

        bool isHisuianForm =
            !string.IsNullOrEmpty(p.formKey)
            && p.formKey.IndexOf("hisui", StringComparison.OrdinalIgnoreCase) >= 0;

        if (p.formKey == "bloodmoon" || p.formKey == "bloodmoonursaluna")
            return false;

        return isHisuianForm || HisuiSpecies.Contains(baseId);
    }

    public static bool IsAlolaUnknown(Pokemon p)
    {
        if (p == null)
            return false;

        return p.id == 808 || p.id == 809; // Meltan, Melmetal
    }

    public static bool IsAlola(Pokemon p) =>
        HasForm(p, "alola") || NameHas(p, "(Alola") || NameHas(p, "Alolan");

    public static bool IsGalarForm(Pokemon p) =>
        HasForm(p, "galar") || NameHas(p, "(Galar") || NameHas(p, "Galarian");

    public static bool IsPaldeaTauros(Pokemon p) => p.formKey == "paldea" && p.baseId == 128;

    public static int IdOrBase(Pokemon p) => p.baseId != 0 ? p.baseId : p.id;

    public static bool IsLumioseMega(Pokemon p) =>
        p != null
        && !string.IsNullOrEmpty(p.formKey)
        && p.formKey.Equals("mega_lumiose", StringComparison.OrdinalIgnoreCase);

    static readonly HashSet<int> HisuiSpecies = new()
    {
        899, // Wyrdeer
        900, // Kleavor
        901, // Ursaluna
        902, // Basculegion
        903, // Sneasler
        904, // Overqwil
        905, // Enamorus
    };

    public static bool IsPaldeaExpedition(Pokemon p)
    {
        if (p == null || p.generation != 9)
            return false;

        int baseId = p.baseId != 0 ? p.baseId : p.id;
        if (PaldeaExpeditionBaseIds.Contains(baseId))
            return true;

        string k = GuessNormalizer.Key(p.name);
        return PaldeaExpeditionNames.Contains(k);
    }

    public static bool IsRegionalForm(Pokemon p)
    {
        if (p == null)
            return false;

        if (IsAlola(p) || IsGalarForm(p) || IsHisui(p))
            return true;

        if (HasForm(p, "paldea") || NameHas(p, "(Paldea") || NameHas(p, "Paldean"))
            return true;

        return false;
    }

    public static bool IsPaldeaExpeditionOrBloodmoon(Pokemon p)
    {
        if (p == null)
            return false;
        if (IsPaldeaExpedition(p))
            return true;

        return string.Equals(p.formKey, "bloodmoon", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.formKey, "bloodmoonursaluna", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly HashSet<int> PaldeaExpeditionBaseIds = new()
    {
        1010,
        1011,
        1012,
        1013,
        1014,
        1015,
        1016,
        1017,
        1018,
        1019,
        1020,
        1021,
        1022,
        1023,
        1024,
        1025,
        901,
    };

    private static readonly HashSet<string> PaldeaExpeditionNames = new()
    {
        "walkingwake",
        "ironleaves",
        "dipplin",
        "poltchageist",
        "sinistcha",
        "okidogi",
        "munkidori",
        "fezandipiti",
        "ogerpon",
        "archaludon",
        "hydrapple",
        "gougingfire",
        "ragingbolt",
        "ironboulder",
        "ironcrown",
        "terapagos",
        "pecharunt",
        "ursalunabloodmoon",
        "ursalunabm",
    };

    public static string GetGenTitle(int generation)
    {
        return generation switch
        {
            0 => "Full Quiz (Gen 1–9)",
            1 => "Kanto (Gen 1)",
            2 => "Johto (Gen 2)",
            3 => "Hoenn (Gen 3)",
            4 => "Sinnoh (Gen 4)",
            5 => "Unova (Gen 5)",
            6 => "Kalos (Gen 6)",
            7 => "Alola (Gen 7)",
            8 => "Galar (Gen 8)",
            9 => "Paldea (Gen 9)",
            _ => $"Generation {generation}",
        };
    }

    public static bool IsGenTitle(string gen)
    {
        return gen.Contains("gen", StringComparison.OrdinalIgnoreCase)
            && !gen.Contains("full quiz", StringComparison.OrdinalIgnoreCase);
    }
}
