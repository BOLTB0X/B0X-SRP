using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class YBotController_Backup : MonoBehaviour
{
    private enum JumpPhase
    {
        None,     // 점프 중 아님
        WindUp,   // 입력 후 ~ 클립 이륙 시점 (무릎 굽히는 구간)
        Air,      // 실제로 공중에 떠 있는 구간
        Landing   // 착지 후 클립이 끝나 Locomotion으로 돌아갈 때까지
    }

    private enum SitPhase
    {
        None,         // 서 있음
        SittingDown,  // 앉는 클립 정방향 재생 중
        Seated,       // 앉은 상태 유지 (클립 끝 자세)
        StandingUp,   // 같은 클립을 끝에서부터 역재생 중
        StandUpBlend  // 역재생 끝 -> Locomotion 블렌딩 중
    }

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3.0f;
    [SerializeField] private float runSpeed = 6.0f;
    [SerializeField] private float acceleration = 20.0f;
    [SerializeField] private float deceleration = 25.0f;
    [SerializeField] private float turnSpeed = 540.0f;
    [SerializeField] private float groundedGravity = -2.0f;

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
    [Tooltip("이 속도 이하일 때만 점프 가능")]
    [SerializeField] private float idleSpeedThreshold = 0.1f;

    [Header("Sit")]
    [SerializeField] private KeyCode sitKey = KeyCode.C;
    [Tooltip("역재생이 이 정규화 시간(0=클립 시작) 이하가 되면 Locomotion으로 블렌딩을 시작")]
    [SerializeField] private float reverseEndNormalizedTime = 0.02f;
    [Tooltip("역재생 끝 -> Locomotion 블렌딩 시간. 이 시간이 지나면 이동 입력을 다시 받을 수 있음")]
    [SerializeField] private float standUpBlendTime = 0.15f;
    [Tooltip("앉기/일어나기 클립이 끝나지 않을 때 강제로 다음 단계로 넘어가는 안전 시간")]
    [SerializeField] private float sitTimeout = 5.0f;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int SitHash = Animator.StringToHash("Sit");
    private static readonly int SitSpeedHash = Animator.StringToHash("SitSpeed");
    private static readonly int SitStateHash = Animator.StringToHash("Sitting Pose");
    private static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    private CharacterController characterController;

    private Vector3 moveDirection = Vector3.forward;
    private float currentSpeed;
    private float verticalVelocity;

    private JumpPhase jumpPhase = JumpPhase.None;
    private float phaseTimer;

    private SitPhase sitPhase = SitPhase.None;
    private bool sitPending;   // 이동 중 앉기를 눌렀을 때: 멈출 때까지 대기
    private float sitTimer;

    // 체공 시간(T)과 높이(h)로부터 계산되는 값
    // 중력 g = 8h / T^2, 초기 속도 v0 = 4h / T  ->  정확히 T초 뒤에 같은 높이로 착지
    private float gravity;
    private float jumpVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        Vector3 center = characterController.center;
        center.y = characterController.height * 0.5f;
        characterController.center = center;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        CacheJumpPhysics();
        ValidateAnimatorParameters();
    }

    private void OnValidate()
    {
        CacheJumpPhysics();
    }

    private void CacheJumpPhysics()
    {
        float airTime = Mathf.Max(0.1f, landingTime - takeoffTime);
        gravity = -8.0f * jumpHeight / (airTime * airTime);
        jumpVelocity = 4.0f * jumpHeight / airTime;
    }

    private void Update()
    {
        // 입력
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 inputDirection = Vector3.ClampMagnitude(
            new Vector3(horizontal, 0.0f, vertical), 1.0f
        );
        bool hasInput = inputDirection.sqrMagnitude > 0.01f;
        bool isRunning = hasInput &&
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

        // isGrounded는 직전 Move 결과이므로 프레임 시작 시 한 번만 읽음
        bool grounded = characterController.isGrounded;

        UpdateJump(grounded);
        UpdateSit(grounded, hasInput);
        Debug.Log("[Sit] Sit=true");

        // 점프/앉기 중에는 수평 입력을 막음 (잠긴 동안 속도는 감속되어 0으로 수렴)
        bool movementLocked = jumpPhase != JumpPhase.None ||
            sitPhase != SitPhase.None ||
            sitPending;
        UpdateHorizontal(inputDirection, hasInput && !movementLocked, isRunning);
        UpdateVertical(grounded);

        Vector3 velocity = moveDirection * currentSpeed;
        velocity.y = verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);

        UpdateAnimator();
    }

    /// <summary>
    /// 점프 단계 관리: 입력 -> (윈드업) -> 이륙 -> 공중 -> 착지
    /// 월드 이륙 시점을 클립의 이륙 시점(takeoffTime)에 맞춤
    /// </summary>
    private void UpdateJump(bool grounded)
    {
        switch (jumpPhase)
        {
            case JumpPhase.None:
                bool canJump = Input.GetKeyDown(KeyCode.Z) &&
                    sitPhase == SitPhase.None &&
                    !sitPending &&
                    grounded &&
                    currentSpeed <= idleSpeedThreshold;

                if (canJump)
                {
                    jumpPhase = JumpPhase.WindUp;
                    phaseTimer = takeoffTime + takeoffDelayOffset;
                    if (animator != null)
                        animator.SetBool(JumpHash, true);
                }
                break;

            case JumpPhase.WindUp:
                phaseTimer -= Time.deltaTime;
                if (phaseTimer <= 0.0f)
                {
                    verticalVelocity = jumpVelocity;
                    jumpPhase = JumpPhase.Air;
                }
                break;

            case JumpPhase.Air:
                // 상승 중(verticalVelocity > 0)에는 착지로 보지 않는다
                if (grounded && verticalVelocity <= 0.0f)
                {
                    jumpPhase = JumpPhase.Landing;
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
    /// 앉기/일어나기 토글. 지상에서만 시작하고, 이동 중이면 먼저 멈춘 뒤 앉는다.
    /// 앉을 때는 Sitting Pose 클립을 정방향으로, 일어날 때는 같은 클립을 끝에서부터 역재생한다.
    /// </summary>
    private void UpdateSit(bool grounded, bool hasInput)
    {
        if (Input.GetKeyDown(sitKey))
            Debug.Log($"[Sit] key={sitKey}, phase={sitPhase}, grounded={grounded}, jumpPhase={jumpPhase}, speed={currentSpeed:F2}, pending={sitPending}", this);

        switch (sitPhase)
        {
            case SitPhase.None:
                if (Input.GetKeyDown(sitKey) && grounded && jumpPhase == JumpPhase.None)
                    sitPending = !sitPending;   // 대기 중 한 번 더 누르면 취소

                if (sitPending && currentSpeed <= idleSpeedThreshold)
                {
                    sitPending = false;
                    sitPhase = SitPhase.SittingDown;
                    sitTimer = sitTimeout;
                    if (animator != null)
                    {
                        animator.SetFloat(SitSpeedHash, 1.0f);   // 정방향 재생
                        animator.SetBool(SitHash, true);
                    }
                }
                break;

            case SitPhase.SittingDown:
                // 앉는 클립이 끝까지 재생되면 앉은 상태로 전환
                sitTimer -= Time.deltaTime;
                if (animator == null || IsSitClipFinished() || sitTimer <= 0.0f)
                    sitPhase = SitPhase.Seated;
                break;

            case SitPhase.Seated:
                if (Input.GetKeyDown(sitKey) || hasInput)
                    BeginStandUp();
                break;

            case SitPhase.StandingUp:
                sitTimer -= Time.deltaTime;
                if (animator == null || IsReverseFinished() || sitTimer <= 0.0f)
                    EndStandUp();
                break;

            case SitPhase.StandUpBlend:
                sitTimer -= Time.deltaTime;
                if (sitTimer <= 0.0f)
                    sitPhase = SitPhase.None;
                break;
        }
    }

    /// <summary>
    /// 클립 끝(정규화 시간 1)에서 시작해 거꾸로 재생
    /// Sit 불은 역재생이 끝날 때까지 true로 유지
    /// </summary>
    private void BeginStandUp()
    {
        sitPhase = SitPhase.StandingUp;
        sitTimer = sitTimeout;

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
        sitPhase = SitPhase.StandUpBlend;
        sitTimer = standUpBlendTime;

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

    /// <summary>
    /// 점프 종료: Jump 불을 내리고, 애니메이터가 아직 Jump 상태에 머물러 있으면
    /// Locomotion으로 직접 복귀시킴 (컨트롤러 전이 설정이 잘못돼도 갇히지 않도록)
    /// </summary>
    private void FinishJump()
    {
        jumpPhase = JumpPhase.None;

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

    /// <summary>
    /// 'Jump', 'Sit', 'SitSpeed' 파라미터와 'Sitting Pose' 상태가 있는지 확인
    /// </summary>
    private void ValidateAnimatorParameters()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        ValidateParameter(JumpHash, "Jump", AnimatorControllerParameterType.Bool);
        ValidateParameter(SitHash, "Sit", AnimatorControllerParameterType.Bool);
        ValidateParameter(SitSpeedHash, "SitSpeed", AnimatorControllerParameterType.Float);

        if (!animator.HasState(0, SitStateHash))
            Debug.LogError("Animator에 'Sitting Pose' 상태가 없음 상태 이름을 확인", this);
    }

    private void ValidateParameter(int nameHash, string parameterName, AnimatorControllerParameterType expected)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash != nameHash)
                continue;

            if (parameter.type != expected)
            {
                Debug.LogError(
                    "Animator의 '" + parameterName + "' 파라미터가 " + expected +
                    "이(가) 아님 (현재: " + parameter.type + ")", this);
            }
            return;
        }

        Debug.LogError("Animator에 '" + parameterName + "' 파라미터가 없음", this);
    }

    /// <summary>
    /// 수평 속도 가감속과 회전. 멈출 때도 마지막 방향으로 감속하며 미끄러진다.
    /// </summary>
    private void UpdateHorizontal(Vector3 inputDirection, bool wantsMove, bool isRunning)
    {
        float targetSpeed = !wantsMove ? 0.0f : isRunning ? runSpeed : walkSpeed;

        if (wantsMove)
        {
            moveDirection = inputDirection.normalized;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(moveDirection),
                turnSpeed * Time.deltaTime
            );
        }

        float rate = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);
    }

    private void UpdateVertical(bool grounded)
    {
        if (grounded && verticalVelocity < 0.0f)
            verticalVelocity = groundedGravity;
        else
            verticalVelocity += gravity * Time.deltaTime;
    }

    /// <summary>
    /// 블렌드트리의 Speed는 실제 수평 속도를 runSpeed로 정규화한 값 (walk = 0.5, run = 1.0)
    /// </summary>
    private void UpdateAnimator()
    {
        if (animator == null)
            return;

        animator.SetFloat(SpeedHash, currentSpeed / runSpeed);
    }
}