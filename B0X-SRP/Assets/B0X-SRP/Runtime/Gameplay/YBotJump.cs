using UnityEngine;

/// <summary>
/// 점프 단계 관리: 입력 -> 윈드업 -> 이륙 -> 공중 -> 착지 -> Locomotion 복귀
/// 시작 시점의 이동 상태(정지/걷기/달리기)에 따라 제자리/걷기/달리기 점프 중 하나를 고름
/// 점프 클립마다 이륙/착지 시점이 다르므로 종류별로 중력과 초기 속도를 따로 계산
/// 시작 가능 여부는 YBotController가 판단해서 넘겨줌
/// </summary>
public class YBotJump : MonoBehaviour
{
    private enum JumpPhase
    {
        None,     // 점프 중 아님
        WindUp,   // 입력 후 ~ 클립 이륙 시점
        Air,      // 실제로 공중에 떠 있는 구간
        Landing   // 착지 후 Locomotion으로 돌아갈 때까지
    }

    /// <summary>
    /// 점프 한 종류의 설정. Animator 파라미터/상태 이름과 클립 기준 타이밍을 묶음
    /// </summary>
    [System.Serializable]
    private class JumpVariant
    {
        [Header("Animator")]
        [Tooltip("Animator Bool 파라미터 이름")]
        public string parameterName = "Jump";
        [Tooltip("Animator 점프 상태 이름 (착지 후 상태에 갇혔는지 판정하는 데 사용)")]
        public string stateName = "Jump";

        [Header("Timing (점프 클립 기준, 단위: 초)")]
        [Tooltip("점프 최고 높이(m)")]
        public float jumpHeight = 0.8f;
        [Tooltip("클립에서 발이 땅을 떠나는 시점")]
        public float takeoffTime = 0.48f;
        [Tooltip("입력 후 애니메이션이 실제로 반응하기까지의 지연 보정. 이륙/착지 모두 이만큼 늦춰짐")]
        public float takeoffDelayOffset = 0.2f;
        [Tooltip("클립에서 발이 땅에 닿는 시점")]
        public float landingTime = 1.06f;
        [Tooltip("착지 모션을 재생한 뒤 Locomotion으로 돌아갈 정규화 시점")]
        [Range(0.0f, 1.0f)]
        public float landingEndNormalizedTime = 0.9f;

        [Header("Landing")]
        [Tooltip("착지 후 최소 이동 잠금 시간")]
        public float landingLockTime = 0.25f;
        [Tooltip("착지 후 Locomotion으로 복귀하는 블렌딩 시간")]
        public float landingBlendTime = 0.15f;

        [Header("Horizontal")]
        [Tooltip("true면 점프 중 수평 속도와 방향을 유지한다 (걷기/달리기 점프). false면 제자리 점프처럼 멈춘 채로 둔다")]
        public bool holdMomentum;

        // 런타임 계산 값 (직렬화하지 않음)
        [System.NonSerialized] public int parameterHash;
        [System.NonSerialized] public int stateHash;
        [System.NonSerialized] public float gravity;
        [System.NonSerialized] public float launchVelocity;

        /// <summary>
        /// 체공 시간(T)과 높이(h)로부터 중력과 초기 속도를 계산
        /// 중력 g = 8h / T^2, 초기 속도 v0 = 4h / T  ->  정확히 T초 뒤에 같은 높이로 착지
        /// </summary>
        public void CachePhysics()
        {
            float airTime = Mathf.Max(0.1f, landingTime - takeoffTime);
            gravity = -8.0f * jumpHeight / (airTime * airTime);
            launchVelocity = 4.0f * jumpHeight / airTime;
        }

        public void CacheHashes()
        {
            parameterHash = Animator.StringToHash(parameterName);
            stateHash = Animator.StringToHash(stateName);
        }
    }

    // 걷기/달리기 점프의 값은 임시값이다. 각 클립에서 발이 떠나는/닿는 시간을 재서 맞춤
    [Header("제자리 점프 (정지 상태)")]
    [SerializeField] private JumpVariant idleJump = new JumpVariant();

    [Header("걷기 점프")]
    [SerializeField] private JumpVariant walkingJump = new JumpVariant
    {
        parameterName = "WalkingJump",
        stateName = "Walking Jump",
        jumpHeight = 0.7f,
        takeoffTime = 0.25f,
        takeoffDelayOffset = 0.0f,
        landingTime = 0.85f,
        landingEndNormalizedTime = 0.8973f,
        landingLockTime = 0.2f,
        landingBlendTime = 0.2f,
        holdMomentum = true
    };

    [Header("달리기 점프")]
    [SerializeField] private JumpVariant runningJump = new JumpVariant
    {
        parameterName = "RunningJump",
        stateName = "Running Jump",
        jumpHeight = 0.9f,
        takeoffTime = 0.2f,
        takeoffDelayOffset = 0.0f,
        landingTime = 0.8f,
        landingEndNormalizedTime = 0.7222f,
        landingLockTime = 0.2f,
        landingBlendTime = 0.2f,
        holdMomentum = true
    };

    private static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    private Animator animator;
    private YBotMovement movement;

    private JumpPhase phase = JumpPhase.None;
    private float phaseTimer;
    private JumpVariant active;   // 현재(또는 마지막으로) 실행한 점프 종류
    private bool pendingBoolClear; // 외부 상태로 넘어간 뒤 Animator가 점프 상태를 벗어나면 Bool을 내릴 예정

    /// <summary>점프 중(윈드업/공중/착지)이면 true. 앉기를 막는 데 사용</summary>
    public bool IsActive => phase != JumpPhase.None;

    /// <summary>이륙 후 착지 전(공중)이면 true. 점프 중 벽을 잡을 수 있는지 판단하는 데 사용</summary>
    public bool IsAirborne => phase == JumpPhase.Air;

    /// <summary>
    /// 점프를 중단하고 다른 상태(벽 매달리기)에 넘김
    /// 점프 Bool은 바로 내리지 않고, Animator가 점프 상태를 벗어난 뒤에 내림
    /// (바로 내리면 Jump -> Locomotion 전이가 매달리기 전이보다 먼저 발동하거나, 나중에 점프가 다시 재생될 수 있음)
    /// </summary>
    public void AbortForExternalState()
    {
        if (phase == JumpPhase.None || active == null)
            return;

        phase = JumpPhase.None;
        pendingBoolClear = true;
    }

    /// <summary>
    /// 점프 중에는 해당 점프 클립 기준 중력, 아니면 제자리 점프 기준 중력 (음수)
    /// </summary>
    public float Gravity => IsActive ? active.gravity : idleJump.gravity;

    /// <summary>
    /// 점프 중 수평 이동을 어떻게 다룰지. 걷기/달리기 점프는 공중에서 속도를 유지하고,
    /// 착지부터는 감속하며, 제자리 점프는 멈춤
    /// </summary>
    public YBotMoveMode HorizontalMode
    {
        get
        {
            if (!IsActive)
                return YBotMoveMode.Free;

            if (phase == JumpPhase.Landing && active.holdMomentum)
                return YBotMoveMode.Brake;

            return active.holdMomentum ? YBotMoveMode.Hold : YBotMoveMode.Stop;
        }
    }

    private void OnValidate()
    {
        idleJump.CachePhysics();
        walkingJump.CachePhysics();
        runningJump.CachePhysics();
    }

    public void Initialize(Animator targetAnimator, YBotMovement targetMovement)
    {
        animator = targetAnimator;
        movement = targetMovement;

        foreach (JumpVariant variant in new[] { idleJump, walkingJump, runningJump })
        {
            variant.CacheHashes();
            variant.CachePhysics();

            YBotAnimatorUtility.ValidateParameter(
                animator, variant.parameterName, AnimatorControllerParameterType.Bool, this);
            YBotAnimatorUtility.ValidateState(animator, variant.stateName, this);
        }
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
                if (pendingBoolClear)
                    ClearAbortedBool();
                else if (startRequested)
                    StartJump();
                break;

            case JumpPhase.WindUp:
                phaseTimer -= Time.deltaTime;
                if (phaseTimer <= 0.0f)
                {
                    movement.Launch(active.launchVelocity);
                    phase = JumpPhase.Air;
                }
                break;

            case JumpPhase.Air:
                // 상승 중(VerticalVelocity > 0)에는 착지로 보지 않읍
                if (grounded && movement.VerticalVelocity <= 0.0f)
                {
                    phase = JumpPhase.Landing;
                    phaseTimer = active.landingLockTime;
                }
                break;

            case JumpPhase.Landing:
                phaseTimer -= Time.deltaTime;
                if (phaseTimer <= 0.0f && HasReachedLandingEnd())
                    FinishJump();
                break;
        }
    }

    /// <summary>
    /// 시작하는 순간의 이동 상태로 점프 종류를 고르고, 해당 Bool 파라미터만 올림
    /// </summary>
    private void StartJump()
    {
        active = SelectVariant(movement.Locomotion);
        phase = JumpPhase.WindUp;
        phaseTimer = active.takeoffTime + active.takeoffDelayOffset;

        if (animator != null)
            animator.SetBool(active.parameterHash, true);
    }

    private JumpVariant SelectVariant(YBotLocomotion locomotion)
    {
        switch (locomotion)
        {
            case YBotLocomotion.Walking: return walkingJump;
            case YBotLocomotion.Running: return runningJump;
            default: return idleJump;
        }
    }

    /// <summary>
    /// 점프 종료: Bool을 내리고, 애니메이터가 아직 해당 점프 상태에 머물러 있으면
    /// Locomotion으로 직접 복귀시킴 (컨트롤러 전이 설정이 잘못돼도 갇히지 않도록)
    /// </summary>
    private void FinishJump()
    {
        phase = JumpPhase.None;

        if (animator == null)
            return;

        // Bool을 내리면 컨트롤러의 해당 점프 -> Locomotion 전이가 발동
        animator.SetBool(active.parameterHash, false);

        if (IsInJumpState())
            animator.CrossFadeInFixedTime(LocomotionHash, active.landingBlendTime, 0);
    }

    /// <summary>
    /// 중단한 점프의 Bool을 Animator가 점프 상태를 벗어난 뒤에 내림
    /// </summary>
    private void ClearAbortedBool()
    {
        if (animator == null)
        {
            pendingBoolClear = false;
            return;
        }

        if (IsInJumpState())
            return;

        animator.SetBool(active.parameterHash, false);
        pendingBoolClear = false;
    }

    private bool HasReachedLandingEnd()
    {
        if (animator == null)
            return true;

        AnimatorStateInfo stateInfo;
        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            if (nextState.shortNameHash == active.stateHash)
            {
                stateInfo = nextState;
            }
            else
            {
                stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.shortNameHash != active.stateHash)
                    return false;
            }
        }
        else
        {
            stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.shortNameHash != active.stateHash)
                return false;
        }

        return stateInfo.normalizedTime >= active.landingEndNormalizedTime;
    }

    private bool IsInJumpState()
    {
        if (animator.IsInTransition(0))
        {
            // 이미 Locomotion으로 나가는 중이면 건드리지 않음
            return animator.GetNextAnimatorStateInfo(0).shortNameHash == active.stateHash;
        }

        return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == active.stateHash;
    }
}