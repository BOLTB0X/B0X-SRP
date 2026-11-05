using UnityEngine;

/// <summary>
/// 점프 단계 관리: 입력 -> 윈드업 -> 이륙 -> 공중 -> 착지 -> Locomotion 복귀
/// 점프 클립의 이륙/착지 시점으로부터 중력과 초기 속도를 계산해 모션과 월드 점프를 맞츰
/// 시작 가능 여부는 YBotController가 판단해서 넘겨줌
/// </summary>
public class YBotJump : MonoBehaviour
{
    private enum JumpPhase
    {
        None,     // 점프 중 아님
        WindUp,   // 입력 후 ~ 클립 이륙 시점 (무릎 굽히는 구간)
        Air,      // 실제로 공중에 떠 있는 구간
        Landing   // 착지 후 클립이 끝나 Locomotion으로 돌아갈 때까지
    }

    [Header("Jump (점프 클립 기준, 단위: 초)")]
    [Tooltip("점프 최고 높이(m)")]
    [SerializeField] private float jumpHeight = 0.8f;
    [Tooltip("클립에서 발이 땅을 떠나는 시점")]
    [SerializeField] private float takeoffTime = 0.58f;
    [Tooltip("입력 후 애니메이션이 실제로 반응하기까지의 지연 보정. 이륙/착지 모두 이만큼 늦춰짐")]
    [SerializeField] private float takeoffDelayOffset = 0.5f;
    [Tooltip("클립에서 발이 땅에 닿는 시점")]
    [SerializeField] private float landingTime = 1.06f;
    [Tooltip("착지 후 이동을 잠그는 시간. 이 시간 동안은 수평 입력을 무시하고 속도는 감속되어 0으로 수렴")]
    [SerializeField] private float landingLockTime = 0.25f;
    [Tooltip("착지 후 Locomotion으로 복귀하는 블렌딩 시간")]
    [SerializeField] private float landingBlendTime = 0.15f;

    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    private Animator animator;
    private YBotMovement movement;

    private JumpPhase phase = JumpPhase.None;
    private float phaseTimer;

    // 체공 시간(T)과 높이(h)로부터 계산되는 값
    // 중력 g = 8h / T^2, 초기 속도 v0 = 4h / T  ->  정확히 T초 뒤에 같은 높이로 착지
    private float gravity;
    private float jumpVelocity;

    /// <summary>
    /// 점프 중(윈드업/공중/착지)이면 true. 이동과 앉기를 막는 데 쓰임
    /// </summary>
    public bool IsActive => phase != JumpPhase.None;

    /// <summary>
    /// 점프 클립 기준으로 계산된 중력 (음수)
    /// </summary>
    public float Gravity => gravity;

    private void OnValidate()
    {
        CacheJumpPhysics();
    }

    public void Initialize(Animator targetAnimator, YBotMovement targetMovement)
    {
        animator = targetAnimator;
        movement = targetMovement;

        CacheJumpPhysics();
        YBotAnimatorUtility.ValidateParameter(animator, "Jump", AnimatorControllerParameterType.Bool, this);
    }

    private void CacheJumpPhysics()
    {
        float airTime = Mathf.Max(0.1f, landingTime - takeoffTime);
        gravity = -8.0f * jumpHeight / (airTime * airTime);
        jumpVelocity = 4.0f * jumpHeight / airTime;
    }

    /// <summary>
    /// 프레임당 한 번 호출
    /// </summary>
    /// <param name="startRequested">점프를 시작해도 되는 입력이 들어왔는지 (조건 판단은 호출자 몫)</param>
    public void Tick(bool grounded, bool startRequested)
    {
        switch (phase)
        {
            case JumpPhase.None:
                if (startRequested)
                {
                    phase = JumpPhase.WindUp;
                    phaseTimer = takeoffTime + takeoffDelayOffset;
                    if (animator != null)
                        animator.SetBool(JumpHash, true);
                }
                break;

            case JumpPhase.WindUp:
                phaseTimer -= Time.deltaTime;
                if (phaseTimer <= 0.0f)
                {
                    movement.Launch(jumpVelocity);
                    phase = JumpPhase.Air;
                }
                break;

            case JumpPhase.Air:
                // 상승 중(VerticalVelocity > 0)에는 착지 X
                if (grounded && movement.VerticalVelocity <= 0.0f)
                {
                    phase = JumpPhase.Landing;
                    phaseTimer = landingLockTime;
                }
                break;

            case JumpPhase.Landing:
                phaseTimer -= Time.deltaTime;
                if (phaseTimer <= 0.0f)
                    FinishJump();
                break;
        }
    }

    /// <summary>
    /// 점프 종료: Jump 불을 내리고, 애니메이터가 아직 Jump 상태에 머물러 있으면
    /// Locomotion으로 직접 복귀시킴 (컨트롤러 전이 설정이 잘못돼도 갇히지 않도록)
    /// </summary>
    private void FinishJump()
    {
        phase = JumpPhase.None;

        if (animator == null)
            return;

        // Jump 불을 내리면 컨트롤러의 Jump -> Locomotion 전이가 발동
        animator.SetBool(JumpHash, false);

        if (IsInJumpState())
            animator.CrossFadeInFixedTime(LocomotionHash, landingBlendTime, 0);
    }

    private bool IsInJumpState()
    {
        if (animator.IsInTransition(0))
        {
            // 이미 Locomotion으로 나가는 중이면 건드리지 않음
            return animator.GetNextAnimatorStateInfo(0).IsName("Jump");
        }

        return animator.GetCurrentAnimatorStateInfo(0).IsName("Jump");
    }
}