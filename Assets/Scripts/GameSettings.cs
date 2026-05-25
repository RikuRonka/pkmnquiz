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
    private static bool quizLaunchArmed;
    public static QuizMultiplayerMode MultiplayerMode { get; set; } = QuizMultiplayerMode.None;
    public static string MultiplayerJoinCode { get; set; }
    public static string MultiplayerNickname { get; set; } = "Player";

    public static bool IsMultiplayer => MultiplayerMode != QuizMultiplayerMode.None;

    public static void ArmQuizLaunch()
    {
        quizLaunchArmed = true;
    }

    public static bool ConsumeQuizLaunchArm()
    {
        bool armed = quizLaunchArmed;
        quizLaunchArmed = false;
        return armed;
    }

    public static void Clear()
    {
        Generation = null;
        TypeFilter = null;
        TypeBgColor = null;
        quizLaunchArmed = false;
    }

    public static void ClearMultiplayer()
    {
        MultiplayerMode = QuizMultiplayerMode.None;
        MultiplayerJoinCode = null;
        MultiplayerNickname = "Player";
        quizLaunchArmed = false;
    }
}
