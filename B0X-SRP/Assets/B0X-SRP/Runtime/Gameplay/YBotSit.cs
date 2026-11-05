using UnityEngine;

/// <summary>
/// 앉기/일어나기 담당
/// 앉을 때는 Sitting Pose 클립을 정방향으로, 일어날 때는 같은 클립을 끝에서부터 역재생
/// 나중에 의자용 앉기/일어나기 클립이 생기면 BeginStandUp / EndStandUp만 바꾸면 됌
/// </summary>
public class YBotSit : MonoBehaviour
{
    private enum SitPhase
    {
        None,         // 서 있음
        SittingDown,  // 앉는 클립 정방향 재생 중
        Seated,       // 앉은 상태 유지 (클립 끝 자세)
        StandingUp,   // 같은 클립을 끝에서부터 역재생 중
        StandUpBlend  // 역재생 끝 -> Locomotion 블렌딩 중
    }

    [Header("Sit")]
    [Tooltip("역재생이 이 정규화 시간(0=클립 시작) 이하가 되면 Locomotion으로 블렌딩을 시작")]
    [SerializeField] private float reverseEndNormalizedTime = 0.02f;
    [Tooltip("역재생 끝 -> Locomotion 블렌딩 시간. 이 시간이 지나면 이동 입력을 다시 받을 수 있음")]
    [SerializeField] private float standUpBlendTime = 0.15f;
    [Tooltip("앉기/일어나기 클립이 끝나지 않을 때 강제로 다음 단계로 넘어가는 안전 시간")]
    [SerializeField] private float sitTimeout = 5.0f;

    private const string SitStateName = "Sitting Pose";

    private static readonly int SitHash = Animator.StringToHash("Sit");
    private static readonly int SitSpeedHash = Animator.StringToHash("SitSpeed");
    private static readonly int SitStateHash = Animator.StringToHash(SitStateName);
    private static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    private Animator animator;

    private SitPhase phase = SitPhase.None;
    private bool pending;   // 이동 중 앉기를 눌렀을 때: 멈출 때까지 대기
    private float timer;

    /// <summary>
    /// 앉는 중, 앉은 상태, 일어나는 중, 또는 멈추길 기다리는 중이면 true. 이동과 점프를 막는 데 쓰임
    /// </summary>
    public bool IsActive => phase != SitPhase.None || pending;

    public void Initialize(Animator targetAnimator)
    {
        animator = targetAnimator;

        YBotAnimatorUtility.ValidateParameter(animator, "Sit", AnimatorControllerParameterType.Bool, this);
        YBotAnimatorUtility.ValidateParameter(animator, "SitSpeed", AnimatorControllerParameterType.Float, this);
        YBotAnimatorUtility.ValidateState(animator, SitStateName, this);
    }

    /// <summary>
    /// 프레임당 한 번 호출
    /// </summary>
    /// <param name="togglePressed">앉기/일어나기 키가 이번 프레임에 눌렸는지</param>
    /// <param name="hasMoveInput">이동 입력이 있는지 (앉은 상태에서 입력하면 일어난다)</param>
    /// <param name="isIdle">캐릭터가 거의 멈춰 있는지</param>
    /// <param name="jumpActive">점프 중인지 (점프 중에는 앉을 수 없다)</param>
    public void Tick(bool grounded, bool togglePressed, bool hasMoveInput, bool isIdle, bool jumpActive)
    {
        switch (phase)
        {
            case SitPhase.None:
                if (togglePressed && grounded && !jumpActive)
                    pending = !pending;   // 대기 중 한 번 더 누르면 취소

                if (pending && isIdle)
                {
                    pending = false;
                    phase = SitPhase.SittingDown;
                    timer = sitTimeout;
                    if (animator != null)
                    {
                        animator.SetFloat(SitSpeedHash, 1.0f);   // 정방향 재생
                        animator.SetBool(SitHash, true);
                    }
                }
                break;

            case SitPhase.SittingDown:
                // 앉는 클립이 끝까지 재생되면 앉은 상태로 전환
                timer -= Time.deltaTime;
                if (animator == null || IsSitClipFinished() || timer <= 0.0f)
                    phase = SitPhase.Seated;
                break;

            case SitPhase.Seated:
                if (togglePressed || hasMoveInput)
                    BeginStandUp();
                break;

            case SitPhase.StandingUp:
                timer -= Time.deltaTime;
                if (animator == null || IsReverseFinished() || timer <= 0.0f)
                    EndStandUp();
                break;

            case SitPhase.StandUpBlend:
                timer -= Time.deltaTime;
                if (timer <= 0.0f)
                    phase = SitPhase.None;
                break;
        }
    }

    /// <summary>
    /// 클립 끝(정규화 시간 1)에서 시작해 거꾸로 재생
    /// Sit 불은 역재생이 끝날 때까지 true로 유지
    /// </summary>
    private void BeginStandUp()
    {
        phase = SitPhase.StandingUp;
        timer = sitTimeout;

        if (animator == null)
            return;

        animator.SetFloat(SitSpeedHash, -1.0f);
        animator.Play(SitStateHash, 0, 1.0f);
    }

    /// <summary>
    /// 역재생이 끝나면 Locomotion으로 블렌딩
    /// SitSpeed는 Locomotion에 도달할 때까지 -1로 두고, 다음에 앉을 때 1로 복원
    /// </summary>
    private void EndStandUp()
    {
        phase = SitPhase.StandUpBlend;
        timer = standUpBlendTime;

        if (animator == null)
            return;

        animator.SetBool(SitHash, false);
        animator.CrossFadeInFixedTime(LocomotionHash, standUpBlendTime, 0);
    }

    private bool IsSitClipFinished()
    {
        if (animator.IsInTransition(0))
            return false;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash == SitStateHash && state.normalizedTime >= 1.0f;
    }

    private bool IsReverseFinished()
    {
        if (animator.IsInTransition(0))
            return false;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash == SitStateHash &&
            state.normalizedTime <= reverseEndNormalizedTime;
    }
}