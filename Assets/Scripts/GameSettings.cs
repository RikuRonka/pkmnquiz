using UnityEngine;

public enum QuizMultiplayerMode
{
    None,
    Host,
    Client,
}

public static class GameSettings
{
    public static int? Generation { get; set; } = null;
    public static string[] TypeFilter;
    public static Color? TypeBgColor;
    public static QuizMultiplayerMode MultiplayerMode { get; set; } = QuizMultiplayerMode.None;
    public static string MultiplayerJoinCode { get; set; }
    public static string MultiplayerNickname { get; set; } = "Player";

    public static bool IsMultiplayer => MultiplayerMode != QuizMultiplayerMode.None;

    public static void Clear()
    {
        Generation = null;
        TypeFilter = null;
        TypeBgColor = null;
    }

    public static void ClearMultiplayer()
    {
        MultiplayerMode = QuizMultiplayerMode.None;
        MultiplayerJoinCode = null;
        MultiplayerNickname = "Player";
    }
}
