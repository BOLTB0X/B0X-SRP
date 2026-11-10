using UnityEngine;

/// <summary>
/// 벽 매달리기/오르기 담당
///
/// 진입: 점프 중(공중) 앞에서 벽 충돌이 감지되면 벽 윗면(걸치는 높이)을 캐스트해서 판정
///   손이 닿는 범위의 모서리 -> Jump To Hang 으로 모서리를 잡고 Hanging Idle
///   손이 닿는 범위보다 높은 벽 -> 벽 표면에 바로 붙어 Hanging Idle
///   윗면이 손이 닿는 범위보다 낮은 장애물 -> 매달리지 않고 평범한 점프로 둠
///
/// 매달린 상태
///   입력 없음 = Hanging Idle, 위/아래 입력 = Climbing Up Wall (벽을 타고 이동)
///   모서리 높이에서 한 번 더 위로 = 위로 올라서기, 점프 키 = 놓기, 옆 입력 = 모서리/벽을 따라 이동
///
/// Animator는 Bool 파라미터(Hanging, LedgeGrab, Climbing)와 전이로 구동하고,
/// 전이 설정이 어긋나 상태에 갇히지 않도록 코드가 보조로 직접 CrossFade함
/// 밀 수 있는 물체(YBotPush.CanPush)는 대상에서 제외
/// </summary>
public class YBotClimb : MonoBehaviour
{
    private enum ClimbPhase
    {
        None,       // 등반 중 아님
        Grabbing,   // 벽/모서리를 잡으러 이동 중 (Jump To Hang 또는 벽 붙기)
        Hanging,    // 매달림. 입력 없음 = Hanging Idle, 위/아래 입력 = Climbing Up Wall
        Mantling,   // 모서리 위로 올라서는 중 (Climbing Up Wall)
        Releasing   // 매달림 해제. Locomotion으로 돌아올 때까지
    }

    [Header("Animator 파라미터")]
    [Tooltip("Bool. 매달림 모드 전체 구간에서 true")]
    [SerializeField] private string hangingParameterName = "Hanging";
    [Tooltip("Bool. 진입할 때 모서리를 잡았으면 true(Jump To Hang), 벽에 바로 붙었으면 false(Hanging Idle)")]
    [SerializeField] private string ledgeGrabParameterName = "LedgeGrab";
    [Tooltip("Bool. 위/아래로 이동하는 동안 true (Hanging Idle <-> Climbing Up Wall)")]
    [SerializeField] private string climbingParameterName = "Climbing";
    [Tooltip("선택. Float. Climbing Up Wall 상태의 Speed Multiplier에 연결하면 내려갈 때 역재생(-1). 비워두거나 없으면 사용 안 함")]
    [SerializeField] private string climbSpeedParameterName = "ClimbSpeed";

    [Header("Animator 상태 이름")]
    [SerializeField] private string jumpToHangStateName = "Jump To Hang";
    [SerializeField] private string hangIdleStateName = "Hanging Idle";
    [SerializeField] private string climbStateName = "Climbing Up Wall";
    [SerializeField] private string locomotionStateName = "Locomotion";

    [Header("Animator 전이 안전장치")]
    [Tooltip("기대한 상태가 이 시간 넘게 재생되지 않으면 코드가 직접 CrossFade")]
    [SerializeField] private float syncGrace = 0.3f;
    [Tooltip("Jump To Hang 이 이 정규화 시간까지 재생됐는데 Hanging Idle로 전이되지 않으면 코드가 직접 넘김")]
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
    [Tooltip("윗면이 이 높이 이하면 모서리를 잡음(Jump To Hang). 이보다 높은 벽은 벽 표면에 바로 붙음")]
    [SerializeField] private float ledgeReachMax = 2.4f;
    [Tooltip("손이 닿는 범위보다 높은 벽에도 붙을 수 있는지")]
    [SerializeField] private bool allowWallCling = true;
    [Tooltip("Jump To Hang 클립 길이에 맞춤")]
    [SerializeField] private float ledgeGrabDuration = 0.85f;
    [Tooltip("벽 표면에 바로 붙을 때 이동 시간")]
    [SerializeField] private float clingGrabDuration = 0.2f;

    [Header("Hang")]
    [Tooltip("매달렸을 때 발에서 모서리(손)까지의 높이(m). 손이 모서리에 안 맞으면 이 값을 조정")]
    [SerializeField] private float hangHeight = 1.35f;
    [Tooltip("매달렸을 때 캡슐과 벽 사이 간격(m)")]
    [SerializeField] private float hangWallGap = 0.05f;
    [Tooltip("벽을 타고 위/아래로 이동하는 속도(m/s)")]
    [SerializeField] private float climbSpeed = 0.8f;
    [Tooltip("벽을 따라 옆으로 이동하는 속도(m/s)")]
    [SerializeField] private float hangMoveSpeed = 1.2f;
    [Tooltip("위/아래 입력으로 인정하는 정렬도 (벽 쪽 = 위, 벽 반대쪽 = 아래)")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float hangInputThreshold = 0.5f;

    [Header("Mantle (모서리 위로 올라서기)")]
    [Tooltip("Climbing Up Wall 클립 길이에 맞춤")]
    [SerializeField] private float mantleDuration = 1.0f;
    [Tooltip("오르는 시간 중 수직으로 올라가는 비율. 나머지는 벽 위로 올라서는 구간")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float mantleRiseRatio = 0.6f;
    [Tooltip("모서리에서 안쪽으로 올라서는 거리(m)")]
    [SerializeField] private float mantleForwardDistance = 0.65f;

    [Header("Rotation")]
    [Tooltip("벽을 향해 몸을 돌리는 속도(도/초)")]
    [SerializeField] private float faceWallSpeed = 720.0f;

    private int hangingHash;
    private int ledgeGrabHash;
    private int climbingHash;
    private int climbSpeedHash;
    private bool hasClimbSpeedParameter;

    private int jumpToHangStateHash;
    private int hangIdleStateHash;
    private int climbStateHash;
    private int locomotionStateHash;

    private CharacterController controller;
    private Animator animator;
    private YBotPush push;

    private ClimbPhase phase = ClimbPhase.None;

    private Collider wallCollider;
    private Vector3 wallNormal;     // 벽 표면 법선 (수평, 플레이어 쪽을 향함)
    private Vector3 wallPoint;      // 벽 표면 위 기준 지점 (옆 이동하면 함께 이동)
    private float ledgeTopY;        // 벽 윗면 높이
    private bool isLedgeHang;       // 모서리를 잡고 진입했는지
    private bool lastClimbing;
    private float syncTimer;
    private float releaseTimer;

    // 보간 이동 경로: 시작 -> (수직 상승) -> 중간 -> (벽 위로 전진) -> 끝
    private Vector3 pathStart;
    private Vector3 pathMid;
    private Vector3 pathEnd;
    private float pathRiseRatio;
    private float traversalTime;
    private float traversalDuration;

    /// <summary>등반(잡는 중, 매달림, 올라서는 중, 해제 중) 중이면 true. 점프/앉기/밀기를 막는 데 사용</summary>
    public bool IsActive => phase != ClimbPhase.None;

    /// <summary>이 컴포넌트가 위치를 직접 움직이는 구간이면 true (이때 YBotMovement는 동작하지 않음)</summary>
    private bool OwnsMovement =>
        phase == ClimbPhase.Grabbing || phase == ClimbPhase.Hanging || phase == ClimbPhase.Mantling;

    public void Initialize(CharacterController characterController, Animator targetAnimator, YBotPush targetPush)
    {
        controller = characterController;
        animator = targetAnimator;
        push = targetPush;

        hangingHash = Animator.StringToHash(hangingParameterName);
        ledgeGrabHash = Animator.StringToHash(ledgeGrabParameterName);
        climbingHash = Animator.StringToHash(climbingParameterName);
        climbSpeedHash = Animator.StringToHash(climbSpeedParameterName);

        jumpToHangStateHash = Animator.StringToHash(jumpToHangStateName);
        hangIdleStateHash = Animator.StringToHash(hangIdleStateName);
        climbStateHash = Animator.StringToHash(climbStateName);
        locomotionStateHash = Animator.StringToHash(locomotionStateName);

        YBotAnimatorUtility.ValidateParameter(animator, hangingParameterName, AnimatorControllerParameterType.Bool, this);
        YBotAnimatorUtility.ValidateParameter(animator, ledgeGrabParameterName, AnimatorControllerParameterType.Bool, this);
        YBotAnimatorUtility.ValidateParameter(animator, climbingParameterName, AnimatorControllerParameterType.Bool, this);
        YBotAnimatorUtility.ValidateState(animator, jumpToHangStateName, this);
        YBotAnimatorUtility.ValidateState(animator, hangIdleStateName, this);
        YBotAnimatorUtility.ValidateState(animator, climbStateName, this);

        hasClimbSpeedParameter = HasParameter(climbSpeedParameterName, AnimatorControllerParameterType.Float);
    }

    private void OnDisable()
    {
        // 오르는 도중에 비활성화돼도 CharacterController가 꺼진 채로 남지 않게 함
        SetControllerEnabled(true);
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
        lastClimbing = false;
        SetAnimatorState(true, isLedgeHang, false);
        SetClimbSpeed(1.0f);
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
        float t = Mathf.Clamp01(traversalTime / traversalDuration);

        transform.position = EvaluatePath(t);
        RotateToWall();

        if (phase == ClimbPhase.Grabbing)
            RescueEntry();
        else
            SyncHangState(true);

        if (t >= 1.0f)
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
            return;
        }

        // 올라서기 끝: 해제하고 Locomotion으로 돌아감
        BeginRelease();
    }

    // ------------------------------------------------------------------
    // 매달림: 입력 없음 = Hanging Idle, 위/아래 = Climbing Up Wall
    // ------------------------------------------------------------------

    private void TickHang(Vector3 inputDirection, bool hasMoveInput, bool jumpPressed)
    {
        // 점프 키: 놓기
        if (jumpPressed)
        {
            BeginRelease();
            return;
        }

        Vector3 inputDir = hasMoveInput ? inputDirection.normalized : Vector3.zero;
        float vertical = Vector3.Dot(inputDir, -wallNormal);   // + = 위(벽 쪽), - = 아래(벽 반대쪽)

        Vector3 position = transform.position;
        float topLimitFeetY = ledgeTopY - hangHeight;           // 손이 모서리에 닿는 발 높이
        bool atLedge = position.y >= topLimitFeetY - 0.02f;

        bool climbingNow = false;
        float climbDirection = 0.0f;

        if (vertical >= hangInputThreshold)
        {
            if (atLedge)
            {
                // 모서리 높이에서 한 번 더 위로: 올라설 자리가 있으면 올라섬
                if (TryBeginMantle())
                    return;
            }
            else
            {
                position.y = Mathf.Min(position.y + climbSpeed * Time.deltaTime, topLimitFeetY);
                transform.position = position;
                climbingNow = true;
                climbDirection = 1.0f;
            }
        }
        else if (vertical <= -hangInputThreshold)
        {
            // 땅에 닿았으면 놓음
            if (IsNearGround())
            {
                BeginRelease();
                return;
            }

            position.y -= climbSpeed * Time.deltaTime;
            transform.position = position;
            climbingNow = true;
            climbDirection = -1.0f;
        }
        else if (hasMoveInput)
        {
            TryShimmy(inputDir);
        }

        SetClimbing(climbingNow, climbDirection);
        SyncHangState(climbingNow);
    }

    /// <summary>
    /// 벽과 평행한 방향으로 이동. 이동할 위치에 벽이 없으면(모서리 끝) 이동하지 않음
    /// </summary>
    private void TryShimmy(Vector3 inputDir)
    {
        Vector3 sideways = Vector3.ProjectOnPlane(inputDir, wallNormal);
        sideways.y = 0.0f;
        if (sideways.sqrMagnitude < 0.0001f)
            return;

        Vector3 step = sideways.normalized * hangMoveSpeed * Time.deltaTime;

        Vector3 handCheck = transform.position + step + Vector3.up * (hangHeight - 0.1f);
        float checkDistance = controller.radius + hangWallGap + 0.3f;
        if (!Physics.Raycast(handCheck, -wallNormal, checkDistance, obstacleLayers, QueryTriggerInteraction.Ignore))
            return;

        transform.position += step;
        wallPoint += step;
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
        Vector3 landing = GetLandingPosition();
        if (!HasLandingSpace(landing))
            return false;

        Vector3 position = transform.position;
        Vector3 mid = new Vector3(position.x, ledgeTopY, position.z);

        BeginTraversal(ClimbPhase.Mantling, mantleDuration, mid, landing, mantleRiseRatio);

        SetClimbing(true, 1.0f);
        return true;
    }

    // ------------------------------------------------------------------
    // 해제
    // ------------------------------------------------------------------

    /// <summary>
    /// 매달림을 끝냄. CharacterController를 다시 켜고 파라미터를 내리면 Animator가 Locomotion으로 돌아감
    /// 이동 제어를 돌려주므로 놓은 경우에는 Movement의 중력으로 떨어짐
    /// </summary>
    private void BeginRelease()
    {
        SetControllerEnabled(true);

        phase = ClimbPhase.Releasing;
        releaseTimer = 0.0f;
        wallCollider = null;

        SetAnimatorState(false, false, false);
        SetClimbSpeed(1.0f);
    }

    private void TickRelease()
    {
        releaseTimer += Time.deltaTime;

        if (IsFullyIn(locomotionStateHash) || releaseTimer >= releaseTimeout)
        {
            phase = ClimbPhase.None;
            return;
        }

        // 전이 설정 문제로 Locomotion에 돌아오지 못하면 직접 돌려보냄
        if (animator != null && releaseTimer >= syncGrace && !animator.IsInTransition(0))
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

    private void SetAnimatorState(bool hanging, bool ledgeGrab, bool climbing)
    {
        lastClimbing = climbing;

        if (animator == null)
            return;

        animator.SetBool(hangingHash, hanging);
        animator.SetBool(ledgeGrabHash, ledgeGrab);
        animator.SetBool(climbingHash, climbing);
    }

    private void SetClimbing(bool climbing, float direction)
    {
        if (animator == null)
            return;

        if (climbing != lastClimbing)
        {
            lastClimbing = climbing;
            animator.SetBool(climbingHash, climbing);
        }

        // 내려갈 때는 같은 클립을 역재생 (Speed Multiplier가 연결돼 있을 때)
        if (climbing)
            SetClimbSpeed(direction < 0.0f ? -1.0f : 1.0f);
        else
            SetClimbSpeed(1.0f);
    }

    private void SetClimbSpeed(float value)
    {
        if (animator != null && hasClimbSpeedParameter)
            animator.SetFloat(climbSpeedHash, value);
    }

    /// <summary>
    /// 매달린 동안 기대하는 상태(입력 없음 = Hanging Idle, 이동 중 = Climbing Up Wall)와 실제 재생 상태가
    /// syncGrace 이상 어긋나면 코드가 직접 CrossFade (전이가 없거나 조건이 맞지 않는 설정 실수 대비)
    /// </summary>
    private void SyncHangState(bool climbingNow)
    {
        if (animator == null)
            return;

        int desired = climbingNow ? climbStateHash : hangIdleStateHash;
        bool matches = IsInOrEntering(desired);

        // 진입 직후 Jump To Hang이 재생 중이면 끝날 때까지 기다림
        if (!matches && !climbingNow && IsInOrEntering(jumpToHangStateHash) && !IsFinished(jumpToHangStateHash))
            matches = true;

        if (matches)
        {
            syncTimer = 0.0f;
            return;
        }

        syncTimer += Time.deltaTime;
        if (syncTimer >= syncGrace)
        {
            syncTimer = 0.0f;
            animator.CrossFadeInFixedTime(desired, stateBlendTime, 0);
        }
    }

    /// <summary>
    /// 잡으러 가는 동안 Animator가 매달림 계열 상태로 들어가지 않으면 기대 상태로 직접 보냄
    /// </summary>
    private void RescueEntry()
    {
        if (animator == null || traversalTime < syncGrace)
            return;

        if (IsInOrEntering(jumpToHangStateHash) ||
            IsInOrEntering(hangIdleStateHash) ||
            IsInOrEntering(climbStateHash))
        {
            return;
        }

        int expected = isLedgeHang ? jumpToHangStateHash : hangIdleStateHash;
        animator.CrossFadeInFixedTime(expected, stateBlendTime, 0);
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

    private bool HasParameter(string parameterName, AnimatorControllerParameterType type)
    {
        if (string.IsNullOrEmpty(parameterName) || !YBotAnimatorUtility.IsReady(animator))
            return false;

        int nameHash = Animator.StringToHash(parameterName);
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == nameHash && parameter.type == type)
                return true;
        }

        return false;
    }

    private static Vector3 Flatten(Vector3 vector)
    {
        vector.y = 0.0f;
        return vector.sqrMagnitude > 0.0001f ? vector.normalized : Vector3.zero;
    }
}