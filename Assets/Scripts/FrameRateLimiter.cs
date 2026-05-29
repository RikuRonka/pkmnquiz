using UnityEngine;

public static class FrameRateLimiter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyMonitorRefreshCap()
    {
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = CurrentDisplayRefreshRate();
    }

    private static int CurrentDisplayRefreshRate()
    {
#if UNITY_2022_2_OR_NEWER
        double refreshRate = Screen.currentResolution.refreshRateRatio.value;
        if (refreshRate > 1d)
            return Mathf.RoundToInt((float)refreshRate);
#endif

#pragma warning disable CS0618
        int legacyRefreshRate = Screen.currentResolution.refreshRate;
#pragma warning restore CS0618
        return legacyRefreshRate > 0 ? legacyRefreshRate : -1;
    }
}
