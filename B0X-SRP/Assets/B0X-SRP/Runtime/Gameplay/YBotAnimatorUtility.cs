using UnityEngine;

/// <summary>
/// Animator 파라미터/상태가 컨트롤러에 올바르게 있는지 시작 시점에 검사하는 헬퍼
/// 이름이나 타입이 어긋나면 Console에 바로 에러를 띄워 원인 찾는 시간을 줄인다
/// </summary>
public static class YBotAnimatorUtility
{
    public static bool IsReady(Animator animator)
    {
        return animator != null && animator.runtimeAnimatorController != null;
    }

    public static void ValidateParameter(
        Animator                        animator,
        string                          parameterName,
        AnimatorControllerParameterType expected,
        Object                          context)
    {
        if (!IsReady(animator))
            return;

        int nameHash = Animator.StringToHash(parameterName);
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash != nameHash)
                continue;

            if (parameter.type != expected)
            {
                Debug.LogError(
                    "Animator의 '" + parameterName + "' 파라미터가 " + expected +
                    " 타입이 X (현재: " + parameter.type + ")", context);
            }
            return;
        }

        Debug.LogError("Animator에 '" + parameterName + "' 파라미터가 없음", context);
    }

    public static void ValidateState(
        Animator animator,
        string   stateName,
        Object   context)
    {
        if (!IsReady(animator))
            return;

        if (!animator.HasState(0, Animator.StringToHash(stateName)))
            Debug.LogError("Animator에 '" + stateName + "' 상태가 없음 ", context);
    }
}