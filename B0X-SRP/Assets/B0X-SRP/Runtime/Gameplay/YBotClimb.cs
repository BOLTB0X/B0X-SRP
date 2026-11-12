using UnityEngine;

/// <summary>
/// 벽 매달리기/오르기 담당
///
/// 진입: 점프 중(공중) 앞에서 벽 충돌이 감지되면 벽 윗면(걸치는 높이)을 캐스트해서 판정
///   손이 닿는 범위의 모서리 -> Jump To Freehang 으로 모서리를 잡고 Hanging Idle
///   손이 닿는 범위보다 높은 벽 -> 벽 표면에 바로 붙어 Hanging Idle
///   윗면이 손이 닿는 범위보다 낮은 장애물 -> 매달리지 않고 평범한 점프로 둠
///
/// 매달린 상태
///   입력 없음 = Hanging Idle, 위/아래 입력 = Climbing Up Wall (벽을 타고 이동)
///   모서리 높이에서 한 번 더 위로 = 위로 올라서기, 점프 키 = 놓기, 옆 입력 = 모서리/벽을 따라 이동
///
/// Animator는 기존 점프/등반 전이 Bool과 Hanging Locomotion의 Speed Blend Tree로 구동
/// 밀 수 있는 물체(YBotPush.CanPush)는 대상에서 제외
/// </summary>
public class YBotClimb : MonoBehaviour
{
    private enum ClimbPhase
    {
        None,       // 등반 중 아님
        Grabbing,   // 벽/모서리를 잡으러 이동 중 (Jump To Hang 또는 벽 붙기)
        Hanging,    // 매달림. 입력 없음 = Hanging Idle, 위/아래 입력 = Climbing Up Wall
        Mantling,   // 모서리 위로 올라서는 중 (Climbing End)
        WallJumpWindUp, // 벽에서 점프: Jump From Wall 모션이 벽을 차고 나가는 시점이 될 때까지 벽에 붙어 있음
        Releasing   // 매달림 해제. Locomotion으로 돌아올 때까지
    }

    [Header("Animator 파라미터")]
    [Tooltip("Bool. Locomotion -> Jump To Freehang -> Hanging Locomotion 진입에 사용")]
    [SerializeField] private string jumpToHangParameterName = "Jump Hanging";
    [Tooltip("Bool. 매달림 해제 시 Jump From Wall 상태를 재생")]
    [SerializeField] private string jumpFromWallParameterName = "Jump From Wall";
    [Tooltip("Bool. 벽 위로 올라선 뒤 Climbing End 상태를 재생")]
    [SerializeField] private string climbingEndParameterName = "Climbing End";
    [Tooltip("Hanging Locomotion Blend Tree의 1D 파라미터 (0=Idle, 중간=Shimmy, 1=Climbing Up Wall)")]
    [SerializeField] private string hangingSpeedParameterName = "Speed";
    [Tooltip("Hanging Locomotion 상태의 Speed Multiplier. 내려갈 때 -1로 설정해 Climbing Up Wall을 역재생")]
    [SerializeField] private string climbPlaybackSpeedParameterName = "ClimbSpeed";

    [Header("Animator 상태 이름")]
    [SerializeField] private string jumpToHangStateName = "Jump To Freehang";
    [SerializeField] private string hangingLocomotionStateName = "Hanging Locomotion";
    [SerializeField] private string jumpFromWallStateName = "Jump From Wall";
    [SerializeField] private string climbingEndStateName = "Climbing End";
    [SerializeField] private string locomotionStateName = "Locomotion";

    [Header("Animator 전이 안전장치")]
    [Tooltip("기대한 상태가 이 시간 넘게 재생되지 않으면 코드가 직접 CrossFade")]
    [SerializeField] private float syncGrace = 0.3f;
    [Tooltip("Jump To Freehang 이 이 정규화 시간까지 재생됐는데 Hanging Locomotion으로 전이되지 않으면 코드가 직접 넘김")]
    [Range(0.5f, 1.0f)]
    [SerializeField] private float stuckNormalizedTime = 0.98f;
    [SerializeField] private float stateBlendTime = 0.12f;
    [Tooltip("해제 후 Locomotion에 돌아오지 못할 때 강제로 끝내는 안전 시간")]
    [SerializeField] private float releaseTimeout = 4.0f;

    [Header("Detection (점프 중 앞쪽 벽 감지)")]
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [Tooltip("캡슐 표면에서 벽까지 이 거리 안이면 감지(m)")]
    [SerializeField] private float contactDistance = 0.3f;
    [SerializeField] private float castRadius = 0.22f;
    [Tooltip("바라보는 방향이 벽 정면에 이 정도 이상 정렬되어야 함 (1 = 정면, 0.7 = 약 45도)")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float startAlignment = 0.7f;
    [Tooltip("벽 윗면을 찾을 때 벽 안쪽으로 파고드는 거리(m)")]
    [SerializeField] private float topProbeInset = 0.15f;
    [Tooltip("올라설 자리 검사에서 윗면과 캡슐 사이 여유(m)")]
    [SerializeField] private float landingCheckGap = 0.05f;

    [Header("Grab (걸치는 길이 판정, 발 기준 윗면 높이 m)")]
    [Tooltip("윗면이 이보다 낮으면 매달리지 않음 (올라서거나 넘을 수 있는 높이)")]
    [SerializeField] private float ledgeReachMin = 1.3f;
    [Tooltip("윗면이 이 높이 이하면 모서리를 잡음(Jump To Freehang). 이보다 높은 벽은 벽 표면에 바로 붙음")]
    [SerializeField] private float ledgeReachMax = 2.4f;
    [Tooltip("손이 닿는 범위보다 높은 벽에도 붙을 수 있는지")]
    [SerializeField] private bool allowWallCling = true;
    [Tooltip("Jump To Freehang 클립 길이에 맞춤")]
    [SerializeField] private float ledgeGrabDuration = 0.85f;
    [Tooltip("벽 표면에 바로 붙을 때 이동 시간")]
    [SerializeField] private float clingGrabDuration = 0.2f;

    [Header("Hang")]
    [Tooltip("매달렸을 때 발에서 모서리(손)까지의 높이(m). 손이 모서리에 안 맞으면 이 값을 조정")]
    [SerializeField] private float hangHeight = 1.35f;
    [Tooltip("매달렸을 때 캡슐과 벽 사이 간격(m)")]
    [SerializeField] private float hangWallGap = 0.05f;
    [Tooltip("손 IK가 모서리 양쪽에 둘 간격(m)")]
    [SerializeField] private float ledgeHandSpacing = 0.42f;
    [Tooltip("모서리 위쪽 손 목표점을 벽 안쪽으로 넣는 거리(m)")]
    [SerializeField] private float ledgeHandInset = 0.06f;
    [Tooltip("손 IK 목표점이 벽 윗면에 닿았다고 보는 높이 여유(m)")]
    [SerializeField] private float handLedgeReachTolerance = 0.12f;
    [Tooltip("Hanging Locomotion의 Speed 파라미터가 Idle/이동 사이를 전환하는 시간")]
    [SerializeField] private float hangingBlendDampTime = 0.08f;
    [Tooltip("벽을 타고 위/아래로 이동하는 속도(m/s)")]
    [SerializeField] private float climbSpeed = 0.8f;
    [Tooltip("벽을 따라 옆으로 이동하는 속도(m/s)")]
    [SerializeField] private float hangMoveSpeed = 1.2f;
    [Tooltip("위/아래 입력으로 인정하는 정렬도 (벽 쪽 = 위, 벽 반대쪽 = 아래)")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float hangInputThreshold = 0.5f;
    [Tooltip("벽을 타고 오를 때 발이 올라갈 수 있는 최대 높이를 모서리 아래 이 거리로 제한(m). 이 안에서 손이 모서리에 닿을 때까지 오름")]
    [SerializeField] private float maxClimbFeetBelowLedge = 0.8f;
    [Header("Mantle (모서리 위로 올라서기)")]
    [Tooltip("Climbing Up Wall 클립 길이에 맞춤")]
    [SerializeField] private float mantleDuration = 1.0f;
    [Tooltip("오르는 시간 중 수직으로 올라가는 비율. 나머지는 벽 위로 올라서는 구간")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float mantleRiseRatio = 0.6f;
    [Tooltip("모서리에서 안쪽으로 올라서는 거리(m)")]
    [SerializeField] private float mantleForwardDistance = 0.65f;
    [Tooltip("Climbing End 시작에 필요한 애니메이션 손 위치와 모서리 IK 목표점 사이의 최대 거리(m)")]
    [SerializeField] private float mantleHandTargetDistance = 0.8f;
    [Tooltip("Climbing End 클립의 시작 위치. 0.2는 0~115프레임 클립의 약 23프레임")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float mantleStartNormalizedTime = 0.2f;
    [Tooltip("true면 올라서기 이동을 Climbing End 모션의 진행도에 맞춤 (모션 길이와 자동으로 맞음). false면 mantleDuration 시간 기준")]
    [SerializeField] private bool mantleFollowsClip = true;
    [Tooltip("Climbing End 모션이 이 정규화 시간에 도달하면 올라서기 이동이 끝남")]
    [Range(0.3f, 1.0f)]
    [SerializeField] private float mantleEndNormalizedTime = 0.85f;

    [Header("Wall Jump")]
    [Tooltip("벽에서 점프할 때 바깥쪽 초기 속도(m/s)")]
    [SerializeField] private float wallJumpOutwardSpeed = 2.5f;
    [Tooltip("벽 점프 최고 높이(m)")]
    [SerializeField] private float wallJumpHeight = 0.8f;
    [Tooltip("벽을 떠나 착지하기까지의 체공 시간(초). Jump From Wall 클립의 공중 구간 길이에 맞춤")]
    [SerializeField] private float wallJumpAirTime = 0.8f;
    [Tooltip("Jump From Wall 클립에서 발이 벽을 차고 나가는 정규화 시간. 이 시점에 실제로 벽을 떠남 (전이의 Transition Offset보다 커야 함)")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float wallJumpTakeoffNormalizedTime = 0.4f;
    [Tooltip("Jump From Wall 모션이 시작되지 않을 때 강제로 이륙하는 안전 시간")]
    [SerializeField] private float wallJumpTimeout = 1.0f;

    [Header("Rotation")]
    [Tooltip("벽을 향해 몸을 돌리는 속도(도/초)")]
    [SerializeField] private float faceWallSpeed = 720.0f;

    private int jumpToHangHash;
    private int jumpFromWallHash;
    private int climbingEndHash;
    private int hangingSpeedHash;
    private int climbPlaybackSpeedHash;

    private int jumpToHangStateHash;
    private int hangingLocomotionStateHash;
    private int jumpFromWallStateHash;
    private int climbingEndStateHash;
    private int locomotionStateHash;

    private CharacterController controller;
    private Animator animator;
    private YBotPush push;
    private YBotMovement movement;

    private ClimbPhase phase = ClimbPhase.None;

    private Collider wallCollider;
    private Vector3 wallNormal;     // 벽 표면 법선 (수평, 플레이어 쪽을 향함)
    private Vector3 wallPoint;      // 벽 표면 위 기준 지점 (옆 이동하면 함께 이동)
    private float ledgeTopY;        // 벽 윗면 높이
    private bool isLedgeHang;       // 모서리를 잡고 진입했는지
    private bool leftHandAtLedge;
    private bool rightHandAtLedge;
    private float leftHandLedgeDistance = float.PositiveInfinity;
    private float rightHandLedgeDistance = float.PositiveInfinity;
    private bool warnedMantleBlocked;
    private bool releaseWithJumpFromWall;
    private bool releaseWithClimbingEnd;
    private bool climbingEndStarted;
    private float syncTimer;
    private float releaseTimer;
    private float wallJumpAirTimer;

    // 보간 이동 경로: 시작 -> (수직 상승) -> 중간 -> (벽 위로 전진) -> 끝
    private Vector3 pathStart;
    private Vector3 pathMid;
    private Vector3 pathEnd;
    private float pathRiseRatio;
    private float traversalTime;
    private float traversalDuration;

    /// <summary>등반(잡는 중, 매달림, 올라서는 중, 해제 중) 중이면 true. 점프/앉기/밀기를 막는 데 사용</summary>
    public bool IsActive => phase != ClimbPhase.None;

    /// <summary>잡기/매달림/올라서기 중 손을 벽에 IK로 고정할지 여부</summary>
    public bool IsHandIKActive => phase == ClimbPhase.Grabbing ||
        phase == ClimbPhase.Hanging || phase == ClimbPhase.Mantling;

    /// <summary>이 컴포넌트가 위치를 직접 움직이는 구간이면 true (이때 YBotMovement는 동작하지 않음)</summary>
    private bool OwnsMovement =>
        phase == ClimbPhase.Grabbing || phase == ClimbPhase.Hanging ||
        phase == ClimbPhase.Mantling || phase == ClimbPhase.WallJumpWindUp;

    private float WallJumpAirTimeSafe => Mathf.Max(0.1f, wallJumpAirTime);

    /// <summary>벽 점프 이륙 수직 속도. 높이(h)와 체공 시간(T)으로 v0 = 4h / T</summary>
    private float WallJumpLaunchSpeed => 4.0f * wallJumpHeight / WallJumpAirTimeSafe;

    /// <summary>벽 점프 클립 기준 중력(음수). g = 8h / T^2. 벽에서 점프해 공중에 있는 동안 YBotController가 사용</summary>
    public float Gravity => -8.0f * wallJumpHeight / (WallJumpAirTimeSafe * WallJumpAirTimeSafe);

    /// <summary>벽에서 점프한 뒤 공중에 있는 동안 true. 이때는 점프 중력 대신 Gravity를 사용</summary>
    public bool OverridesGravity => phase == ClimbPhase.Releasing && releaseWithJumpFromWall;

    public void Initialize(CharacterController characterController, Animator targetAnimator, YBotPush targetPush, YBotMovement targetMovement)
    {
        controller = characterController;
        animator = targetAnimator;
        push = targetPush;
        movement = targetMovement;

        jumpToHangHash = Animator.StringToHash(jumpToHangParameterName);
        jumpFromWallHash = Animator.StringToHash(jumpFromWallParameterName);
        climbingEndHash = Animator.StringToHash(climbingEndParameterName);
        hangingSpeedHash = Animator.StringToHash(hangingSpeedParameterName);
        climbPlaybackSpeedHash = Animator.StringToHash(climbPlaybackSpeedParameterName);

        jumpToHangStateHash = Animator.StringToHash(jumpToHangStateName);
        hangingLocomotionStateHash = Animator.StringToHash(hangingLocomotionStateName);
        jumpFromWallStateHash = Animator.StringToHash(jumpFromWallStateName);
        climbingEndStateHash = Animator.StringToHash(climbingEndStateName);
        locomotionStateHash = Animator.StringToHash(locomotionStateName);

        YBotAnimatorUtility.ValidateParameter(animator, jumpToHangParameterName, AnimatorControllerParameterType.Bool, this);
        YBotAnimatorUtility.ValidateParameter(animator, jumpFromWallParameterName, AnimatorControllerParameterType.Bool, this);
        YBotAnimatorUtility.ValidateParameter(animator, climbingEndParameterName, AnimatorControllerParameterType.Bool, this);
        YBotAnimatorUtility.ValidateParameter(animator, hangingSpeedParameterName, AnimatorControllerParameterType.Float, this);
        YBotAnimatorUtility.ValidateState(animator, jumpToHangStateName, this);
        YBotAnimatorUtility.ValidateState(animator, hangingLocomotionStateName, this);
        YBotAnimatorUtility.ValidateState(animator, jumpFromWallStateName, this);
        YBotAnimatorUtility.ValidateState(animator, climbingEndStateName, this);

        YBotAnimatorUtility.ValidateParameter(animator, climbPlaybackSpeedParameterName, AnimatorControllerParameterType.Float, this);
        SetHangBlend(0.0f, 1.0f);
    }

    /// <summary>
    /// YBotHandIK에서 좌우 손의 벽/모서리 접촉점을 구한다.
    /// 모서리 매달림은 고정된 두 지점을, 높은 벽에 붙은 경우는 애니메이션 손 높이에서 벽을 찾는다.
    /// </summary>
    public bool TryGetHandIKTarget(
        AvatarIKGoal goal,
        Vector3 animatedHandPosition,
        float surfaceOffset,
        out Vector3 targetPosition,
        out Quaternion targetRotation,
        out bool isLedgeContact)
    {
        targetPosition = animatedHandPosition;
        targetRotation = Quaternion.identity;
        isLedgeContact = false;

        if (!IsHandIKActive || wallCollider == null)
            return false;

        if (isLedgeHang || animatedHandPosition.y >= ledgeTopY - handLedgeReachTolerance)
        {
            float side = goal == AvatarIKGoal.LeftHand ? -0.5f : 0.5f;
            // 벽을 타다 모서리 높이에 닿은 경우에도 애니메이션 손 위치(벽 앞쪽일 수 있음)가 아니라
            // 벽 기준 지점에서 안쪽으로 넣은 점으로 윗면을 찾아야 광선이 윗면에 닿음
            Vector3 ledgePoint = wallPoint - wallNormal * ledgeHandInset + transform.right * (ledgeHandSpacing * side);
            ledgePoint.y = ledgeTopY;

            Vector3 rayOrigin = ledgePoint + Vector3.up * 0.35f;
            if (!wallCollider.Raycast(new Ray(rayOrigin, Vector3.down), out RaycastHit topHit, 0.6f))
                return false;

            targetPosition = topHit.point + topHit.normal * surfaceOffset;
            targetRotation = Quaternion.LookRotation(Vector3.down, -wallNormal);
            isLedgeContact = true;
            return true;
        }

        Vector3 wallRayOrigin = animatedHandPosition + wallNormal * 0.55f;
        if (!wallCollider.Raycast(new Ray(wallRayOrigin, -wallNormal), out RaycastHit wallHit, 1.2f))
            return false;

        targetPosition = wallHit.point + wallHit.normal * surfaceOffset;
        targetRotation = Quaternion.LookRotation(-wallHit.normal, Vector3.up);
        return true;
    }

    /// <summary>벽을 타는 동안 발 IK 목표를 벽 표면에서 조금 띄운 위치로 구한다.</summary>
    public bool TryGetFootIKTarget(
        Vector3 animatedFootPosition,
        float surfaceOffset,
        out Vector3 targetPosition,
        out Quaternion targetRotation)
    {
        targetPosition = animatedFootPosition;
        targetRotation = Quaternion.identity;

        if (!IsHandIKActive || phase == ClimbPhase.Mantling || wallCollider == null)
            return false;

        Vector3 origin = animatedFootPosition + wallNormal * 0.55f;
        if (!wallCollider.Raycast(new Ray(origin, -wallNormal), out RaycastHit hit, 1.2f))
            return false;

        targetPosition = hit.point + hit.normal * surfaceOffset;
        targetRotation = Quaternion.LookRotation(-hit.normal, Vector3.up);
        return true;
    }

    /// <summary>IK 접촉 여부와 애니메이션 손 위치에서 모서리 목표점까지의 거리를 저장한다.</summary>
    public void SetHandLedgeContact(AvatarIKGoal goal, bool atLedge, float distanceToLedgeTarget)
    {
        if (goal == AvatarIKGoal.LeftHand)
        {
            leftHandAtLedge = atLedge;
            leftHandLedgeDistance = atLedge ? distanceToLedgeTarget : float.PositiveInfinity;
        }
        else if (goal == AvatarIKGoal.RightHand)
        {
            rightHandAtLedge = atLedge;
            rightHandLedgeDistance = atLedge ? distanceToLedgeTarget : float.PositiveInfinity;
        }
    }

    private void OnDisable()
    {
        // 오르는 도중에 비활성화돼도 CharacterController가 꺼진 채로 남지 않게 함
        SetControllerEnabled(true);
        SetAnimatorFlags(false, false, false);
        SetHangBlend(0.0f, 1.0f);
    }

    /// <summary>
    /// 프레임당 한 번 호출
    /// </summary>
    /// <param name="blocked">앉기/밀기 중이라 등반을 시작할 수 없는지</param>
    /// <param name="jumpAirborne">점프로 공중에 떠 있는지 (이때만 벽을 잡을 수 있음)</param>
    /// <returns>이 컴포넌트가 이번 프레임의 이동을 맡으면 true</returns>
    public bool Tick(Vector3 inputDirection, bool hasMoveInput, bool jumpPressed, bool blocked, bool jumpAirborne)
    {
        switch (phase)
        {
            case ClimbPhase.Grabbing:
            case ClimbPhase.Mantling:
                TickTraversal();
                return OwnsMovement;

            case ClimbPhase.Hanging:
                TickHang(inputDirection, hasMoveInput, jumpPressed);
                if (phase == ClimbPhase.Hanging)
                    RotateToWall();
                return OwnsMovement;

            case ClimbPhase.WallJumpWindUp:
                TickWallJumpWindUp();
                return OwnsMovement;

            case ClimbPhase.Releasing:
                TickRelease();
                return false;
        }

        return TickDetect(blocked, jumpAirborne);
    }

    // ------------------------------------------------------------------
    // 진입: 점프 중 벽 감지 + 걸치는 높이 캐스트
    // ------------------------------------------------------------------

    private bool TickDetect(bool blocked, bool jumpAirborne)
    {
        if (blocked || !jumpAirborne)
            return false;

        Vector3 forward = Flatten(transform.forward);
        if (forward.sqrMagnitude < 0.0001f)
            return false;

        if (!TryFindWall(forward, out RaycastHit wall))
            return false;

        Vector3 normal = Flatten(wall.normal);
        if (normal.sqrMagnitude < 0.0001f || Vector3.Dot(forward, -normal) < startAlignment)
            return false;

        float feetY = transform.position.y;
        float topY = FindTopY(wall.collider, wall.point, -normal);

        // 윗면이 손이 닿는 범위보다 낮으면 매달리지 않고 평범한 점프로 둠
        if (topY < feetY + ledgeReachMin)
            return false;

        // 손이 닿는 범위 안이면 모서리를 잡고, 더 높은 벽이면 벽 표면에 바로 붙음
        bool ledge = topY <= feetY + ledgeReachMax;
        if (!ledge && !allowWallCling)
            return false;

        wallCollider = wall.collider;
        wallNormal = normal;
        wallPoint = wall.point;
        ledgeTopY = topY;
        isLedgeHang = ledge;
        leftHandAtLedge = false;
        rightHandAtLedge = false;
        leftHandLedgeDistance = float.PositiveInfinity;
        rightHandLedgeDistance = float.PositiveInfinity;
        warnedMantleBlocked = false;

        BeginGrab();
        return true;
    }

    /// <summary>
    /// 바라보는 방향으로 SphereCast를 쏴 벽을 찾음. 밀 수 있는 물체와 윗면/아랫면은 제외
    /// </summary>
    private bool TryFindWall(Vector3 direction, out RaycastHit hit)
    {
        Vector3 origin = controller.bounds.center;

        // 캡슐 표면에서 contactDistance 안에 있는 벽만 감지
        float distance = Mathf.Max(0.01f, controller.radius + contactDistance - castRadius);
        if (!Physics.SphereCast(
                origin,
                castRadius,
                direction,
                out hit,
                distance,
                obstacleLayers,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.collider.transform.IsChildOf(transform) || Mathf.Abs(hit.normal.y) > 0.5f)
            return false;

        // 밀 수 있는 물체는 YBotPush가 담당 (크기/무게 기준을 YBotPush 하나로 통일)
        return push == null || !push.CanPush(hit.collider);
    }

    /// <summary>
    /// 벽 윗면 높이. 벽 안쪽으로 파고든 지점 위에서 아래로 쏨 (실패하면 콜라이더 경계 상단)
    /// </summary>
    private float FindTopY(Collider collider, Vector3 point, Vector3 inward)
    {
        Bounds bounds = collider.bounds;

        Vector3 origin = point + inward * topProbeInset;
        origin.y = bounds.max.y + 0.1f;

        if (collider.Raycast(new Ray(origin, Vector3.down), out RaycastHit top, bounds.size.y + 0.3f))
            return top.point.y;

        return bounds.max.y;
    }

    private void BeginGrab()
    {
        Vector3 hangPosition = GetHangPosition(isLedgeHang);
        float duration = isLedgeHang ? ledgeGrabDuration : clingGrabDuration;

        BeginTraversal(ClimbPhase.Grabbing, duration, hangPosition, hangPosition, 1.0f);

        syncTimer = 0.0f;
        releaseWithJumpFromWall = false;
        releaseWithClimbingEnd = false;
        SetAnimatorFlags(isLedgeHang, false, false);
        SetHangBlend(0.0f, 1.0f);

        // 모서리 잡기는 Controller의 Locomotion -> Jump To Freehang 전이를 사용한다.
        // 높은 벽에 바로 붙는 경우에는 일회성 점프 클립 없이 Hanging Locomotion으로 간다.
        if (!isLedgeHang && animator != null && animator.HasState(0, hangingLocomotionStateHash))
            animator.CrossFadeInFixedTime(hangingLocomotionStateHash, stateBlendTime, 0);
    }

    // ------------------------------------------------------------------
    // 위치 보간 (잡으러 가기 / 올라서기)
    // ------------------------------------------------------------------

    private void BeginTraversal(ClimbPhase newPhase, float duration, Vector3 mid, Vector3 end, float riseRatio)
    {
        phase = newPhase;

        traversalDuration = Mathf.Max(0.05f, duration);
        traversalTime = 0.0f;
        pathStart = transform.position;
        pathMid = mid;
        pathEnd = end;
        pathRiseRatio = riseRatio;

        // 스크립트가 위치를 직접 움직이는 동안은 충돌에 걸리지 않도록 끔
        SetControllerEnabled(false);
    }

    private void TickTraversal()
    {
        traversalTime += Time.deltaTime;
        float t = phase == ClimbPhase.Mantling && mantleFollowsClip
            ? GetMantleProgress()
            : Mathf.Clamp01(traversalTime / traversalDuration);

        transform.position = EvaluatePath(t);
        RotateToWall();

        if (phase == ClimbPhase.Grabbing)
            RescueEntry();
        else if (phase == ClimbPhase.Mantling)
        {
            if (animator != null)
            {
                if (IsInOrEntering(climbingEndStateHash))
                    climbingEndStarted = true;
                else if (traversalTime >= syncGrace)
                {
                    animator.CrossFadeInFixedTime(
                        climbingEndStateHash, stateBlendTime, 0, 0.0f, mantleStartNormalizedTime);
                    climbingEndStarted = true;
                }
            }
        }

        // 모션 진행도 기준이면 모션이 시작되지 않는 경우를 대비해 안전 시간(mantleDuration의 3배)으로 끝냄
        bool timedOut = phase == ClimbPhase.Mantling && mantleFollowsClip &&
            traversalTime >= traversalDuration * 3.0f;

        if (t >= 1.0f || timedOut)
            FinishTraversal();
    }

    private Vector3 EvaluatePath(float t)
    {
        if (pathRiseRatio >= 1.0f)
            return Vector3.Lerp(pathStart, pathEnd, Mathf.SmoothStep(0.0f, 1.0f, t));

        if (t < pathRiseRatio)
        {
            // 오르는 구간은 일정한 속도
            return Vector3.Lerp(pathStart, pathMid, t / pathRiseRatio);
        }

        float u = (t - pathRiseRatio) / (1.0f - pathRiseRatio);
        return Vector3.Lerp(pathMid, pathEnd, Mathf.SmoothStep(0.0f, 1.0f, u));
    }

    private void FinishTraversal()
    {
        transform.position = pathEnd;

        if (phase == ClimbPhase.Grabbing)
        {
            // 매달림 시작. CharacterController는 계속 꺼 둠
            phase = ClimbPhase.Hanging;
            syncTimer = 0.0f;
            if (animator != null)
                animator.SetBool(jumpToHangHash, false);
            SetHangBlend(0.0f, 1.0f);
            return;
        }

        // 올라서기 끝: 해제하고 Locomotion으로 돌아감 (Climbing End 모션은 컨트롤러 전이가 끝까지 재생)
        BeginRelease(true);
    }

    // ------------------------------------------------------------------
    // 매달림: 입력 없음 = Hanging Idle, 위/아래 = Climbing Up Wall
    // ------------------------------------------------------------------

    private void TickHang(Vector3 inputDirection, bool hasMoveInput, bool jumpPressed)
    {
        // 점프 키: 벽에서 점프
        if (jumpPressed)
        {
            BeginWallJump();
            return;
        }

        Vector3 inputDir = hasMoveInput ? inputDirection.normalized : Vector3.zero;
        float vertical = Vector3.Dot(inputDir, -wallNormal);   // + = 위(벽 쪽), - = 아래(벽 반대쪽)

        Vector3 position = transform.position;
        // 벽에 붙어 오를 때 발이 올라갈 수 있는 최대 높이 (손이 모서리에 닿으면 그 전에 멈추고 올라서기)
        // 인스펙터 여유값이 캡슐 반경보다 커도 손이 모서리에 닿기 전에 멈추지 않게 한다.
        float feetClearance = Mathf.Min(maxClimbFeetBelowLedge, controller.radius + landingCheckGap);
        float topLimitFeetY = ledgeTopY - feetClearance;

        // 손 IK가 실제로 큐브 윗면에 닿았는지 확인한다.
        bool handsAtLedge = HandsReachedLedge();

        float blendSpeed = 0.0f;
        float climbDirection = 1.0f;

        if (vertical >= hangInputThreshold)
        {
            if (handsAtLedge)
            {
                // 손이 실제로 모서리에 닿았을 때만 올라선다.
                if (TryBeginMantle())
                    return;
            }
            else if (position.y < topLimitFeetY)
            {
                position.y = Mathf.Min(position.y + climbSpeed * Time.deltaTime, topLimitFeetY);
                transform.position = position;
                blendSpeed = 1.0f;
            }
        }
        else if (vertical <= -hangInputThreshold)
        {
            // 땅에 닿았으면 놓음
            if (IsNearGround())
            {
                BeginRelease(false);
                return;
            }

            position.y -= climbSpeed * Time.deltaTime;
            transform.position = position;
            climbDirection = -1.0f;
            blendSpeed = 1.0f;
        }
        else if (hasMoveInput)
        {
            blendSpeed = TryShimmy(inputDir);
        }

        SetHangBlend(blendSpeed, climbDirection);
        SyncHangState();
    }

    /// <summary>
    /// 벽과 평행한 방향으로 이동. 이동할 위치에 벽이 없으면(모서리 끝) 이동하지 않음
    /// </summary>
    private float TryShimmy(Vector3 inputDir)
    {
        Vector3 sideways = Vector3.ProjectOnPlane(inputDir, wallNormal);
        sideways.y = 0.0f;
        if (sideways.sqrMagnitude < 0.0001f)
            return 0.0f;

        Vector3 step = sideways.normalized * hangMoveSpeed * Time.deltaTime;

        Vector3 handCheck = transform.position + step + Vector3.up * (hangHeight - 0.1f);
        float checkDistance = controller.radius + hangWallGap + 0.3f;
        if (!Physics.Raycast(handCheck, -wallNormal, checkDistance, obstacleLayers, QueryTriggerInteraction.Ignore))
            return 0.0f;

        transform.position += step;
        wallPoint += step;

        // 1D Blend Tree: 0=Idle, 1/3=왼쪽 shimmy, 2/3=오른쪽 shimmy, 1=등반
        return Vector3.Dot(sideways.normalized, transform.right) < 0.0f
            ? 1.0f / 3.0f
            : 2.0f / 3.0f;
    }

    private bool IsNearGround()
    {
        return Physics.Raycast(
            transform.position + Vector3.up * 0.2f,
            Vector3.down,
            0.25f,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);
    }

    private bool TryBeginMantle()
    {
        if (!TryFindLanding(out Vector3 landing))
        {
            if (!warnedMantleBlocked)
            {
                warnedMantleBlocked = true;
                Debug.LogWarning(
                    "손은 모서리에 닿았지만 올라설 자리가 없어 올라서지 못했습니다. " +
                    "윗면에 다른 벽/물체가 가까운지, mantleForwardDistance가 큰지 확인하세요.", this);
            }
            return false;
        }

        Vector3 position = transform.position;
        Vector3 mid = new Vector3(position.x, ledgeTopY, position.z);

        climbingEndStarted = false;
        BeginTraversal(ClimbPhase.Mantling, mantleDuration, mid, landing, mantleRiseRatio);
        SetAnimatorFlags(false, false, true);
        return true;
    }

    // ------------------------------------------------------------------
    // 해제
    // ------------------------------------------------------------------

    /// <summary>
    /// 매달림을 끝냄 (땅에 내려오기, 올라서기 끝). CharacterController를 다시 켜고 파라미터를 정리하면
    /// Animator가 Locomotion으로 돌아감. 이동 제어를 돌려주므로 Movement의 중력으로 내려옴
    /// </summary>
    /// <param name="playClimbingEnd">true면 Climbing End 모션을 끝까지 재생한 뒤 Locomotion으로 복귀</param>
    private void BeginRelease(bool playClimbingEnd)
    {
        SetControllerEnabled(true);

        phase = ClimbPhase.Releasing;
        releaseTimer = 0.0f;
        wallJumpAirTimer = 0.0f;
        wallCollider = null;
        releaseWithJumpFromWall = false;
        releaseWithClimbingEnd = playClimbingEnd;

        SetAnimatorFlags(false, false, false);
        SetHangBlend(0.0f, 1.0f);
    }

    /// <summary>
    /// 벽에서 점프 시작: Jump From Wall 모션을 재생하고, 실제로 벽을 떠나는 건 모션이 벽을 차고 나가는 시점
    /// (wallJumpTakeoffNormalizedTime). 모션 앞부분의 매달린 자세는 컨트롤러 전이의 Transition Offset으로 건너뜀
    /// </summary>
    private void BeginWallJump()
    {
        phase = ClimbPhase.WallJumpWindUp;
        releaseTimer = 0.0f;

        SetAnimatorFlags(false, true, false);
        SetHangBlend(0.0f, 1.0f);
    }

    private void TickWallJumpWindUp()
    {
        releaseTimer += Time.deltaTime;
        RotateToWall();

        if (animator == null)
        {
            LaunchFromWall();
            return;
        }

        // 전이가 없거나 조건이 맞지 않아 모션이 시작되지 않으면 직접 재생
        if (!IsInOrEntering(jumpFromWallStateHash) && releaseTimer >= syncGrace)
            animator.CrossFadeInFixedTime(jumpFromWallStateHash, stateBlendTime, 0);

        bool reachedTakeoff = GetStateNormalizedTime(jumpFromWallStateHash) >= wallJumpTakeoffNormalizedTime;
        if (reachedTakeoff || releaseTimer >= wallJumpTimeout)
            LaunchFromWall();
    }

    /// <summary>
    /// 벽을 차고 나감: 이 순간부터 물리로 날아가고, 바깥 방향 속도와 클립 기준 높이/체공 시간의 수직 속도를 줌
    /// </summary>
    private void LaunchFromWall()
    {
        SetControllerEnabled(true);

        if (movement != null)
        {
            Vector3 outward = wallNormal.normalized * wallJumpOutwardSpeed;
            movement.Launch(outward + Vector3.up * WallJumpLaunchSpeed);
        }

        phase = ClimbPhase.Releasing;
        releaseTimer = 0.0f;
        wallJumpAirTimer = 0.0f;
        wallCollider = null;
        releaseWithJumpFromWall = true;
        releaseWithClimbingEnd = false;
    }

    private void TickRelease()
    {
        releaseTimer += Time.deltaTime;

        if (animator == null)
        {
            phase = ClimbPhase.None;
            return;
        }

        if (releaseWithJumpFromWall)
        {
            wallJumpAirTimer += Time.deltaTime;

            // 착지하면 Jump From Wall 불을 내려 Locomotion으로 복귀
            // (모션이 먼저 끝나도 마지막 자세를 유지하다가 착지하고, 모션이 끝나기 전에 착지하면 바로 복귀)
            bool landed = wallJumpAirTimer > 0.1f && movement != null &&
                movement.IsGrounded && movement.VerticalVelocity <= 0.0f;

            if (landed)
                animator.SetBool(jumpFromWallHash, false);
            else if (!IsInOrEntering(jumpFromWallStateHash) && releaseTimer >= syncGrace)
                animator.CrossFadeInFixedTime(jumpFromWallStateHash, stateBlendTime, 0);
        }
        else if (releaseWithClimbingEnd)
        {
            if (IsInOrEntering(climbingEndStateHash))
            {
                climbingEndStarted = true;
                animator.SetBool(climbingEndHash, false);   // 재진입을 막고 Exit Time 전이가 복귀하도록 함
            }
            else if (!climbingEndStarted && releaseTimer >= syncGrace)
            {
                animator.CrossFadeInFixedTime(
                    climbingEndStateHash, stateBlendTime, 0, 0.0f, mantleStartNormalizedTime);
                climbingEndStarted = true;
            }
        }

        if (IsFullyIn(locomotionStateHash) &&
            (!releaseWithClimbingEnd || climbingEndStarted))
        {
            SetAnimatorFlags(false, false, false);
            phase = ClimbPhase.None;
            return;
        }

        if (releaseTimer >= releaseTimeout)
        {
            animator.CrossFadeInFixedTime(locomotionStateHash, stateBlendTime, 0);
            SetAnimatorFlags(false, false, false);
            phase = ClimbPhase.None;
            return;
        }

        // 액션 상태를 재생하지 않는 일반 해제이거나 전이가 끊긴 경우 Locomotion으로 복귀
        bool waitingForAction = releaseWithJumpFromWall || releaseWithClimbingEnd;
        if (!waitingForAction && releaseTimer >= syncGrace && !animator.IsInTransition(0))
            animator.CrossFadeInFixedTime(locomotionStateHash, stateBlendTime, 0);
    }

    // ------------------------------------------------------------------
    // 위치 계산 / 공간 검사
    // ------------------------------------------------------------------

    /// <summary>
    /// 매달렸을 때 캡슐 위치: 벽 앞쪽. 모서리를 잡을 때는 손이 모서리에 닿는 높이, 벽에 붙을 때는 현재 높이
    /// </summary>
    private Vector3 GetHangPosition(bool atLedge)
    {
        Vector3 position = wallPoint + wallNormal * (controller.radius + hangWallGap);
        position.y = atLedge ? ledgeTopY - hangHeight : transform.position.y;
        return position;
    }

    /// <summary>벽 위에 올라섰을 때 캡슐 위치</summary>
    private Vector3 GetLandingPosition()
    {
        Vector3 position = wallPoint - wallNormal * mantleForwardDistance;
        position.y = ledgeTopY;
        return position;
    }

    /// <summary>
    /// 올라섰을 때 캡슐이 들어갈 공간이 비어 있고, 발 밑에 바닥이 있는지 확인
    /// </summary>
    private bool HasLandingSpace(Vector3 landing)
    {
        float radius = controller.radius * 0.9f;
        Vector3 bottom = landing + Vector3.up * (radius + landingCheckGap);
        Vector3 top = landing + Vector3.up * (controller.height - radius);

        if (Physics.CheckCapsule(bottom, top, radius, obstacleLayers, QueryTriggerInteraction.Ignore))
            return false;

        return Physics.Raycast(
            landing + Vector3.up * 0.3f,
            Vector3.down,
            0.6f,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);
    }

    private void RotateToWall()
    {
        Quaternion target = Quaternion.LookRotation(-wallNormal, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            target,
            faceWallSpeed * Time.deltaTime
        );
    }

    private void SetControllerEnabled(bool value)
    {
        if (controller != null && controller.enabled != value)
            controller.enabled = value;
    }

    // ------------------------------------------------------------------
    // Animator 파라미터 / 상태
    // ------------------------------------------------------------------

    private void SetAnimatorFlags(bool jumpToHang, bool jumpFromWall, bool climbingEnd)
    {
        if (animator == null)
            return;

        animator.SetBool(jumpToHangHash, jumpToHang);
        animator.SetBool(jumpFromWallHash, jumpFromWall);
        animator.SetBool(climbingEndHash, climbingEnd);
    }

    private void SetHangBlend(float blendSpeed, float climbDirection)
    {
        if (animator == null)
            return;

        animator.SetFloat(
            hangingSpeedHash,
            Mathf.Clamp01(blendSpeed),
            hangingBlendDampTime,
            Time.deltaTime);
        animator.SetFloat(climbPlaybackSpeedHash, climbDirection < 0.0f ? -1.0f : 1.0f);
    }

    /// <summary>
    /// 매달린 동안 실제 상태가 Hanging Locomotion이 아니면 코드가 복귀시킴.
    /// Blend Tree 내부 동작은 Speed 값으로 선택한다.
    /// </summary>
    private void SyncHangState()
    {
        if (animator == null)
            return;

        if (IsInOrEntering(hangingLocomotionStateHash))
        {
            syncTimer = 0.0f;
            return;
        }

        // 진입 직후 Jump To Hang이 재생 중이면 끝날 때까지 기다림
        if (IsInOrEntering(jumpToHangStateHash) && !IsFinished(jumpToHangStateHash))
        {
            syncTimer = 0.0f;
            return;
        }

        syncTimer += Time.deltaTime;
        if (syncTimer >= syncGrace)
        {
            syncTimer = 0.0f;
            animator.CrossFadeInFixedTime(hangingLocomotionStateHash, stateBlendTime, 0);
        }
    }

    /// <summary>
    /// 잡으러 가는 동안 Animator가 매달림 계열 상태로 들어가지 않으면 기대 상태로 직접 보냄
    /// </summary>
    private void RescueEntry()
    {
        if (animator == null || traversalTime < syncGrace)
            return;

        int expected = isLedgeHang ? jumpToHangStateHash : hangingLocomotionStateHash;
        if (IsInOrEntering(expected))
            return;

        animator.CrossFadeInFixedTime(expected, stateBlendTime, 0);
    }

    /// <summary>
    /// 올라서기 이동 진행도(0~1): Climbing End 모션의 정규화 시간에 맞춤. 모션이 아직 시작되지 않았으면 0(시작 위치에서 대기)
    /// 모션 길이를 바꿔도 이동이 자동으로 따라가서 위치와 모션이 어긋나지 않음
    /// </summary>
    private float GetMantleProgress()
    {
        if (animator == null)
            return Mathf.Clamp01(traversalTime / traversalDuration);

        float normalized = GetStateNormalizedTime(climbingEndStateHash);
        if (normalized < 0.0f)
            return 0.0f;

        float progressRange = Mathf.Max(0.01f, mantleEndNormalizedTime - mantleStartNormalizedTime);
        return Mathf.Clamp01((normalized - mantleStartNormalizedTime) / progressRange);
    }

    /// <summary>해당 상태(또는 그 상태로 들어가는 중)의 정규화 시간. 해당 상태가 아니면 -1</summary>
    private float GetStateNormalizedTime(int stateHash)
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (current.shortNameHash == stateHash)
            return current.normalizedTime;

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (next.shortNameHash == stateHash)
                return next.normalizedTime;
        }

        return -1.0f;
    }

    /// <summary>손 IK가 실제로 모서리 윗면에 닿았는지 확인한다.</summary>
    private bool HandsReachedLedge()
    {
        return (leftHandAtLedge && leftHandLedgeDistance <= mantleHandTargetDistance) ||
            (rightHandAtLedge && rightHandLedgeDistance <= mantleHandTargetDistance);
    }

    /// <summary>
    /// 올라설 자리를 찾음. 윗면에 다른 벽이 가까우면 기본 거리에서는 캡슐이 걸리므로 거리를 줄여가며 탐색
    /// </summary>
    private bool TryFindLanding(out Vector3 landing)
    {
        for (int i = 0; i < 3; i++)
        {
            float distance = mantleForwardDistance * (1.0f - i * 0.25f);

            landing = wallPoint - wallNormal * distance;
            landing.y = ledgeTopY;

            if (HasLandingSpace(landing))
                return true;
        }

        landing = Vector3.zero;
        return false;
    }

    private bool IsInOrEntering(int stateHash)
    {
        if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
            return true;

        return animator.IsInTransition(0) &&
            animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
    }

    private bool IsFullyIn(int stateHash)
    {
        if (animator == null)
            return true;

        return !animator.IsInTransition(0) &&
            animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash;
    }

    private bool IsFinished(int stateHash)
    {
        if (animator.IsInTransition(0))
            return false;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.shortNameHash == stateHash && info.normalizedTime >= stuckNormalizedTime;
    }

    private static Vector3 Flatten(Vector3 vector)
    {
        vector.y = 0.0f;
        return vector.sqrMagnitude > 0.0001f ? vector.normalized : Vector3.zero;
    }
}