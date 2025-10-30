using System;

public static class Helpers
{
    public static bool HasForm(Pokemon p, string key) =>
        p != null
        && !string.IsNullOrEmpty(p.formKey)
        && p.formKey.Equals(key, StringComparison.OrdinalIgnoreCase);

    public static bool NameHas(Pokemon p, string needle) =>
        p != null
        && !string.IsNullOrEmpty(p.name)
        && p.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool IsMega(Pokemon p) =>
        HasForm(p, "mega") || HasForm(p, "megax") || HasForm(p, "megay") || NameHas(p, "Mega ");

    public static bool IsGmax(Pokemon p) =>
        HasForm(p, "gmax") || HasForm(p, "gigantamax") || NameHas(p, "Gigantamax");

    public static bool IsHisui(Pokemon p) =>
        HasForm(p, "hisui") || NameHas(p, "(Hisui") || NameHas(p, "Hisuian");

    public static bool IsAlola(Pokemon p) =>
        HasForm(p, "alola") || NameHas(p, "(Alola") || NameHas(p, "Alolan");

    public static bool IsGalarForm(Pokemon p) =>
        HasForm(p, "galar") || NameHas(p, "(Galar") || NameHas(p, "Galarian");

    public static string GetGenTitle(int gen) =>
        gen switch
        {
            1 => "Kanto (Gen 1)",
            2 => "Johto (Gen 2)",
            3 => "Hoenn (Gen 3)",
            4 => "Sinnoh (Gen 4)",
            5 => "Unova (Gen 5)",
            6 => "Kalos (Gen 6)",
            7 => "Alola (Gen 7)",
            8 => "Galar (Gen 8)",
            9 => "Paldea (Gen 9)",
            _ => $"Gen {gen}",
        };
}
