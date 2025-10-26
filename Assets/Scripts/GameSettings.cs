public static class GameSettings
{
    // What to play
    public static int? Generation;          // e.g., 1..9, null = keep default in scene
    public static string[] TypeFilter;      // optional: e.g., ["Fire"] or ["Water","Ice"]

    // (optional) other flags you might add later
    public static bool DexOrder = true;     // true: dex order, false: shuffle
    public static int Minutes = 35;         // timer minutes, 0/negative = infinite

    public static void Clear()
    {
        Generation = null;
        TypeFilter = null;
    }
}
