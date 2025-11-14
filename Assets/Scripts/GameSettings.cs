using UnityEngine;

public static class GameSettings
{
    public static int? Generation { get; set; } = null;
    public static string[] TypeFilter;
    public static Color? TypeBgColor;

    public static void Clear()
    {
        Generation = null;
        TypeFilter = null;
    }
}
