public static class GameSettings
{
    public static int? Generation;
    public static string[] TypeFilter;

    public static bool DexOrder = true;
    public static int Minutes = 35;

    public static void Clear()
    {
        Generation = null;
        TypeFilter = null;
    }
}
