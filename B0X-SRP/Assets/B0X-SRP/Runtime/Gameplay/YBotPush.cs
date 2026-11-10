using UnityEngine;

/// <summary>
/// 밀기 담당
/// 이동 입력 방향으로 SphereCast를 쏴서 앞의 물체를 감지하고,
/// 조건(Rigidbody가 있고, 충분히 작고 가벼움)을 만족하면 Push 파라미터를 켜서 밀기 시작
///
/// 시작/반복/멈춤 모션의 연결(전이 조건, 시간, 블렌딩)은 Animator 컨트롤러에서 직접 조정하고,
/// 코드는 Push Bool만 켜고 끈다. 코드가 애니메이터를 기다리는 방식은 다음과 같다
///   Start   : Push = true. Pushing 상태에 들어갈 때까지 이동 강제 정지
///   Pushing : 물체를 밀면서 이동
///   Stop    : Push = false. Locomotion으로 돌아올 때까지 이동 강제 정지
///
/// 감지는 Raycast 계열로 하고, 실제로 물체를 움직이는 건 CharacterController 충돌 콜백에서 처리
/// </summary>
public class YBotPush : MonoBehaviour
{
    private enum PushPhase
    {
        None,     // 밀지 않음
        Start,    // Push = true. 시작 모션이 끝나 Pushing 상태에 들어갈 때까지 정지
        Pushing,  // 물체를 밀면서 이동 중
        Stop      // Push = false. 멈춤 모션이 끝나 Locomotion으로 돌아올 때까지 정지
    }

    [Header("Animator")]
    [Tooltip("밀기를 켜고 끄는 Animator Bool 파라미터 이름")]
    [SerializeField] private string pushParameterName = "Push";
    [Tooltip("이 상태에 들어가면 이동을 시작 (시작 모션이 끝나고 반복 모션으로 넘어간 상태)")]
    [SerializeField] private string pushingStateName = "Pushing";
    [Tooltip("이 상태로 돌아오면 밀기가 완전히 끝난 것으로 봄 (멈춤 모션이 끝나고 돌아가는 상태)")]
    [SerializeField] private string locomotionStateName = "Locomotion";
    [Tooltip("애니메이터가 기대한 상태에 도달하지 못할 때 강제로 다음 단계로 넘어가는 안전 시간")]
    [SerializeField] private float phaseTimeout = 4.0f;
    [Tooltip("안전 시간이 지나 강제로 Locomotion으로 되돌릴 때의 블렌딩 시간")]
    [SerializeField] private float fallbackBlendTime = 0.2f;

    [Header("Animator 전이 안전장치")]
    [Tooltip("시작 모션 상태 이름. 비워두면 이 안전장치를 쓰지 않음")]
    [SerializeField] private string pushStartStateName = "Push Start";
    [Tooltip("멈춤 모션 상태 이름. 비워두면 이 안전장치를 쓰지 않음")]
    [SerializeField] private string pushStopStateName = "Push Stop";
    [Tooltip("시작/멈춤 모션이 이 정규화 시간까지 재생됐는데 컨트롤러 전이가 일어나지 않았으면 코드가 직접 다음 상태로 넘김")]
    [Range(0.5f, 1.0f)]
    [SerializeField] private float stuckNormalizedTime = 0.98f;
    [Tooltip("Push = false인데 Pushing 상태에서 이 시간 넘게 나가지 못하면 코드가 직접 멈춤 상태로 넘김")]
    [SerializeField] private float stopTransitionGrace = 0.5f;
    [Tooltip("안전장치가 직접 상태를 넘길 때의 블렌딩 시간")]
    [SerializeField] private float stuckBlendTime = 0.15f;

    [Header("Detection")]
    [Tooltip("감지 대상 레이어")]
    [SerializeField] private LayerMask pushableLayers = ~0;
    [Tooltip("캡슐 표면에서 앞쪽으로 감지하는 거리(m)")]
    [SerializeField] private float detectDistance = 0.3f;
    [Tooltip("감지 구체 반지름(m). 두꺼울수록 물체 모서리를 덜 놓침")]
    [SerializeField] private float castRadius = 0.2f;
    [Tooltip("감지 높이 보정. 캡슐 중심 기준")]
    [SerializeField] private float castHeightOffset = 0.0f;

    [Header("Conditions")]
    [Tooltip("물체 높이가 (캐릭터 높이 x 이 값) 이하일 때만 밂. 1이면 캐릭터와 같거나 작은 물체")]
    [SerializeField] private float maxHeightRatio = 1.0f;
    [Tooltip("이 질량 이하만 밂")]
    [SerializeField] private float maxPushMass = 60.0f;
    [Tooltip("밀기 시작에 필요한 입력 정렬도 (1 = 정면, 0.8 = 약 37도)")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float startAlignment = 0.8f;
    [Tooltip("밀기 유지에 필요한 입력 정렬도. 시작보다 낮게 둬서 경계에서 떨리지 않게 함")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float stayAlignment = 0.5f;
    [Tooltip("물체를 놓쳐도 이 시간 동안은 밀기를 유지")]
    [SerializeField] private float lostTargetGrace = 0.15f;

    [Header("Push")]
    [Tooltip("가벼운 물체(질량 0)를 밀 때의 캐릭터 속도")]
    [SerializeField] private float lightPushSpeed = 1.6f;
    [Tooltip("무거운 물체(최대 질량)를 밀 때의 캐릭터 속도")]
    [SerializeField] private float heavyPushSpeed = 0.7f;

    private int pushParameterHash;
    private int pushingStateHash;
    private int locomotionStateHash;
    private int pushStartStateHash;
    private int pushStopStateHash;
    private bool hasStartState;
    private bool hasStopState;

    private Animator animator;
    private CharacterController characterController;

    private PushPhase phase = PushPhase.None;
    private float timer;
    private float lostTimer;

    private Rigidbody target;
    private Collider targetCollider;
    private Vector3 direction = Vector3.forward;
    private float pushSpeed;

    /// <summary>밀기 시작/진행/멈춤 중이면 true. 점프와 앉기를 막는 데 사용</summary>
    public bool IsActive => phase != PushPhase.None;

    /// <summary>손을 물체에 붙이는 구간(시작 + 밀기)이면 true. YBotHandIK가 사용</summary>
    public bool IsGripping => phase == PushPhase.Start || phase == PushPhase.Pushing;

    /// <summary>밀고 있는 물체의 콜라이더 (손을 붙이는 구간이 아니면 null). 손이 닿을 표면을 찾는 데 사용</summary>
    public Collider TargetCollider => targetCollider;

    /// <summary>밀 방향 (수평, 정규화). 물체 표면 법선의 반대 방향으로 고정</summary>
    public Vector3 Direction => direction;

    /// <summary>캐릭터가 이동할 속도. 시작/멈춤 구간은 0 (이동 강제 정지)</summary>
    public float Speed => phase == PushPhase.Pushing ? pushSpeed : 0.0f;

    /// <summary>
    /// 밀기 관련 구간(시작/밀기/멈춤)에는 항상 Push 모드. 속도 0이 넘어오면 YBotMovement가 즉시 정지시킨다
    /// </summary>
    public YBotMoveMode HorizontalMode => YBotMoveMode.Push;

    public void Initialize(Animator targetAnimator, CharacterController controller)
    {
        animator = targetAnimator;
        characterController = controller;

        pushParameterHash = Animator.StringToHash(pushParameterName);
        pushingStateHash = Animator.StringToHash(pushingStateName);
        locomotionStateHash = Animator.StringToHash(locomotionStateName);

        pushStartStateHash = Animator.StringToHash(pushStartStateName);
        pushStopStateHash = Animator.StringToHash(pushStopStateName);
        hasStartState = HasOptionalState(pushStartStateName, pushStartStateHash);
        hasStopState = HasOptionalState(pushStopStateName, pushStopStateHash);

        YBotAnimatorUtility.ValidateParameter(animator, pushParameterName, AnimatorControllerParameterType.Bool, this);
        YBotAnimatorUtility.ValidateState(animator, pushingStateName, this);
        YBotAnimatorUtility.ValidateState(animator, locomotionStateName, this);
    }

    /// <summary>
    /// 프레임당 한 번 호출
    /// </summary>
    /// <param name="inputDirection">카메라 기준으로 변환된 수평 이동 입력</param>
    /// <param name="blocked">점프/앉기 중이라 밀기를 시작할 수 없는지</param>
    public void Tick(bool grounded, Vector3 inputDirection, bool hasMoveInput, bool blocked)
    {
        switch (phase)
        {
            case PushPhase.None:
                if (blocked || !grounded || !hasMoveInput)
                    break;

                Vector3 inputDir = inputDirection.normalized;
                if (TryFindTarget(inputDir, out RaycastHit hit, out Rigidbody body))
                {
                    Vector3 pushDir = -Flatten(hit.normal);
                    if (pushDir.sqrMagnitude > 0.0001f &&
                        Vector3.Dot(inputDir, pushDir) >= startAlignment)
                    {
                        BeginStart(body, hit.collider, pushDir);
                    }
                }
                break;

            case PushPhase.Start:
                timer -= Time.deltaTime;
                if (!KeepPushing(grounded, inputDirection, hasMoveInput))
                    BeginStop();
                else if (IsEnteringOrIn(pushingStateHash) || timer <= 0.0f)
                    phase = PushPhase.Pushing;
                else
                    RescueIfStuck(pushStartStateHash, hasStartState, pushingStateHash);
                break;

            case PushPhase.Pushing:
                if (!KeepPushing(grounded, inputDirection, hasMoveInput))
                    BeginStop();
                break;

            case PushPhase.Stop:
                timer -= Time.deltaTime;
                if (IsFullyIn(locomotionStateHash))
                {
                    phase = PushPhase.None;
                }
                else if (timer <= 0.0f)
                {
                    // 전이 설정 문제 등으로 Locomotion에 못 돌아오면 직접 복귀시킴 (갇힘 방지)
                    if (animator != null)
                        animator.CrossFadeInFixedTime(locomotionStateHash, fallbackBlendTime, 0);
                    phase = PushPhase.None;
                }
                else
                {
                    RescueStop();
                }
                break;
        }
    }

    /// <summary>
    /// Push 파라미터를 켜고 이동을 강제로 정지. 밀기 속도는 물체 무게로 결정
    /// </summary>
    private void BeginStart(Rigidbody body, Collider bodyCollider, Vector3 pushDirection)
    {
        target = body;
        targetCollider = bodyCollider;
        direction = pushDirection;
        lostTimer = 0.0f;

        // 무거울수록 느리게 민다
        float massRatio = Mathf.InverseLerp(0.0f, maxPushMass, body.mass);
        pushSpeed = Mathf.Lerp(lightPushSpeed, heavyPushSpeed, massRatio);

        phase = PushPhase.Start;
        timer = phaseTimeout;

        if (animator != null)
            animator.SetBool(pushParameterHash, true);
    }

    /// <summary>
    /// Push 파라미터를 끄고 물체를 멈춤. 이동은 Locomotion으로 돌아올 때까지 강제 정지
    /// </summary>
    private void BeginStop()
    {
        StopTarget();
        target = null;
        targetCollider = null;

        phase = PushPhase.Stop;
        timer = phaseTimeout;

        if (animator != null)
            animator.SetBool(pushParameterHash, false);
    }

    /// <summary>
    /// 선택 상태가 컨트롤러에 있는지 확인. 이름을 지정했는데 없으면 경고하고 해당 안전장치를 끔
    /// </summary>
    private bool HasOptionalState(string stateName, int stateHash)
    {
        if (string.IsNullOrEmpty(stateName) || !YBotAnimatorUtility.IsReady(animator))
            return false;

        if (animator.HasState(0, stateHash))
            return true;

        Debug.LogWarning("Animator에 '" + stateName + "' 상태가 없어 이 상태의 전이 안전장치를 쓰지 않습니다.", this);
        return false;
    }

    /// <summary>
    /// 시작/멈춤 모션이 거의 끝까지 재생됐는데도 컨트롤러 전이가 일어나지 않으면 다음 상태로 직접 넘김
    /// (조건이 없는 전이는 Has Exit Time이 꺼져 있으면 절대 발동하지 않으므로, 그런 설정 실수에서도 갇히지 않게 함)
    /// </summary>
    private void RescueIfStuck(int stateHash, bool hasState, int nextStateHash)
    {
        if (animator == null || !hasState || animator.IsInTransition(0))
            return;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != stateHash || info.normalizedTime < stuckNormalizedTime)
            return;

        animator.CrossFadeInFixedTime(nextStateHash, stuckBlendTime, 0);
    }

    /// <summary>
    /// 멈춤 구간의 안전장치
    /// 1) Push = false인데 Pushing에서 나가지 못하면 멈춤 모션(없으면 Locomotion)으로 직접 넘김
    /// 2) 멈춤 모션이 끝났는데 Locomotion으로 전이되지 않으면 Locomotion으로 직접 넘김
    /// </summary>
    private void RescueStop()
    {
        if (animator == null)
            return;

        bool elapsedGrace = phaseTimeout - timer > stopTransitionGrace;
        if (elapsedGrace && IsFullyIn(pushingStateHash))
        {
            int next = hasStopState ? pushStopStateHash : locomotionStateHash;
            animator.CrossFadeInFixedTime(next, stuckBlendTime, 0);
            return;
        }

        RescueIfStuck(pushStopStateHash, hasStopState, locomotionStateHash);
    }

    /// <summary>
    /// 해당 상태에 있거나 그 상태로 들어가는 중이면 true
    /// </summary>
    private bool IsEnteringOrIn(int stateHash)
    {
        if (animator == null)
            return true;

        if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
            return true;

        return animator.IsInTransition(0) &&
            animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
    }

    /// <summary>
    /// 블렌딩이 끝나고 해당 상태에 완전히 들어와 있으면 true
    /// </summary>
    private bool IsFullyIn(int stateHash)
    {
        if (animator == null)
            return true;

        return !animator.IsInTransition(0) &&
            animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash;
    }

    /// <summary>
    /// 입력이 계속 물체 쪽이고, 물체가 여전히 앞에 있으면 밀기를 유지
    /// </summary>
    private bool KeepPushing(bool grounded, Vector3 inputDirection, bool hasMoveInput)
    {
        if (!grounded || !hasMoveInput || target == null)
            return false;

        if (Vector3.Dot(inputDirection.normalized, direction) < stayAlignment)
            return false;

        if (TryFindTarget(direction, out _, out Rigidbody body) && body == target)
            lostTimer = 0.0f;
        else
            lostTimer += Time.deltaTime;

        return lostTimer <= lostTargetGrace;
    }

    /// <summary>
    /// 캡슐 중심에서 direction 방향으로 SphereCast를 쏴 밀 수 있는 물체를 찾음
    /// 단일 Raycast는 물체 모서리를 놓치기 쉬워서 반지름이 있는 SphereCast를 사용
    /// </summary>
    private bool TryFindTarget(Vector3 castDirection, out RaycastHit hit, out Rigidbody body)
    {
        body = null;

        Vector3 origin = characterController.bounds.center + Vector3.up * castHeightOffset;

        // SphereCast의 거리는 구체 중심이 이동하는 거리. 구체 앞면이 (반지름 + 감지 거리)까지 닿도록 맞춤
        float distance = Mathf.Max(0.01f, characterController.radius + detectDistance - castRadius);

        if (!Physics.SphereCast(
                origin,
                castRadius,
                castDirection,
                out hit,
                distance,
                pushableLayers,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        // 자기 자신이나 윗면/아랫면은 제외 (옆면만 민다)
        if (hit.collider.transform.IsChildOf(transform))
            return false;
        if (Mathf.Abs(hit.normal.y) > 0.5f)
            return false;

        if (!CanPush(hit.collider))
            return false;

        body = hit.collider.attachedRigidbody;
        return true;
    }

    /// <summary>
    /// 이 콜라이더가 밀 수 있는 물체인지 (Rigidbody가 있고, 캐릭터보다 크지 않고, 너무 무겁지 않음)
    /// 밀기 감지와 등반 감지가 같은 기준을 쓰도록 공개 (밀 수 있는 물체는 오르지 않고 밀고, 나머지는 오름)
    /// </summary>
    public bool CanPush(Collider collider)
    {
        if (collider == null || characterController == null)
            return false;

        Rigidbody body = collider.attachedRigidbody;
        if (body == null || body.isKinematic)
            return false;

        if (body.mass > maxPushMass)
            return false;

        return collider.bounds.size.y <= characterController.height * maxHeightRatio + 0.01f;
    }

    /// <summary>
    /// CharacterController.Move가 물체와 부딪힐 때 호출됨 (이 오브젝트에 CharacterController가 있어야 함)
    /// CharacterController는 Rigidbody를 스스로 밀지 못하므로 여기서 속도를 직접 줌
    /// 시작/멈춤 구간에는 밀지 않고, Pushing 구간에서만 밀음
    /// </summary>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (phase != PushPhase.Pushing || target == null)
            return;

        if (hit.collider.attachedRigidbody != target)
            return;

        // 발밑 충돌은 무시
        if (hit.moveDirection.y < -0.3f)
            return;

        Vector3 velocity = GetVelocity(target);
        velocity.x = direction.x * pushSpeed;
        velocity.z = direction.z * pushSpeed;
        SetVelocity(target, velocity);
    }

    /// <summary>
    /// 밀기를 멈출 때 물체의 수평 속도를 지움
    /// </summary>
    private void StopTarget()
    {
        if (target == null)
            return;

        Vector3 velocity = GetVelocity(target);
        velocity.x = 0.0f;
        velocity.z = 0.0f;
        SetVelocity(target, velocity);
    }

    private static Vector3 GetVelocity(Rigidbody body)
    {
#if UNITY_6000_0_OR_NEWER
        return body.linearVelocity;
#else
        return body.velocity;
#endif
    }

    private static void SetVelocity(Rigidbody body, Vector3 velocity)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = velocity;
#else
        body.velocity = velocity;
#endif
    }

    private static Vector3 Flatten(Vector3 vector)
    {
        vector.y = 0.0f;
        return vector.sqrMagnitude > 0.0001f ? vector.normalized : Vector3.zero;
    }

    /// <summary>
    /// 선택했을 때 감지 구체를 보여줌 (Scene 뷰에서 감지 거리/높이 튜닝용)
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        CharacterController controller = characterController != null
            ? characterController
            : GetComponent<CharacterController>();
        if (controller == null)
            return;

        Vector3 origin = controller.bounds.center + Vector3.up * castHeightOffset;
        Vector3 forward = phase == PushPhase.None ? transform.forward : direction;
        float distance = Mathf.Max(0.01f, controller.radius + detectDistance - castRadius);

        Gizmos.color = phase == PushPhase.None ? Color.yellow : Color.green;
        Gizmos.DrawWireSphere(origin, castRadius);
        Gizmos.DrawLine(origin, origin + forward * distance);
        Gizmos.DrawWireSphere(origin + forward * distance, castRadius);
    }
}