using UnityEngine;

/// <summary>
/// VSync를 유지하면서 표시 주사율이 높은 기기에서도 최대 프레임을 60 이하로 제한한다.
/// VSync 주기를 정수 배수로만 설정할 수 있으므로 60으로 나누어떨어지지 않는 주사율에서는
/// 60보다 낮은 값으로 제한될 수 있다.
/// </summary>
internal static class B0XFrameRateSettings
{
    private const int MaximumFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        Application.targetFrameRate = MaximumFrameRate;

        double refreshRate = Screen.currentResolution.refreshRateRatio.value;
        if (refreshRate <= 0.0)
        {
            QualitySettings.vSyncCount = 1;
            return;
        }

        // VSync가 켜져 있으면 Desktop에서 targetFrameRate는 무시되므로
        // 가능한 VSync 간격 중 60 FPS를 넘지 않는 가장 짧은 간격을 선택한다.
        QualitySettings.vSyncCount = System.Math.Max(
            1,
            (int)System.Math.Ceiling(refreshRate / MaximumFrameRate));
    }
}
