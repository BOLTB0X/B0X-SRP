using UnityEngine;

/// <summary>
/// 입력을 읽고 Movement / Jump / Sit / Push 기능을 조율하는 진입점
/// Update는 여기에만 있고, 각 기능은 Tick으로 호출되므로 실행 순서가 항상 고정
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(YBotMovement))]
[RequireComponent(typeof(YBotJump))]
[RequireComponent(typeof(YBotSit))]
[RequireComponent(typeof(YBotPush))]
[RequireComponent(typeof(YBotClimb))]
public class YBotController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode jumpKey = KeyCode.Z;
    [SerializeField] private KeyCode sitKey = KeyCode.C;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private CharacterController characterController;
    private YBotMovement movement;
    private YBotJump jump;
    private YBotSit sit;
    private YBotPush push;
    private YBotClimb climb;
    private ThirdPersonCamera thirdPersonCamera;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        thirdPersonCamera = FindAnyObjectByType<ThirdPersonCamera>();
        movement = GetOrAdd<YBotMovement>();
        jump = GetOrAdd<YBotJump>();
        sit = GetOrAdd<YBotSit>();
        push = GetOrAdd<YBotPush>();
        climb = GetOrAdd<YBotClimb>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        EnsureHandIK();

        movement.Initialize(characterController, animator);
        jump.Initialize(animator, movement);
        sit.Initialize(animator);
        push.Initialize(animator, characterController);
        climb.Initialize(characterController, animator, push, movement);
    }

    /// <summary>
    /// 기존 오브젝트에 컴포넌트가 아직 없어도 동작하도록 보충
    /// 인스펙터에서 값을 튜닝하려면 에디터에서 직접 붙여두는 편이 좋음
    /// </summary>
    private T GetOrAdd<T>() where T : Component
    {
        if (!TryGetComponent(out T component))
            component = gameObject.AddComponent<T>();

        return component;
    }

    /// <summary>
    /// 손 IK는 Animator가 붙은 오브젝트(Y Bot 모델)에 있어야 동작함
    /// 없으면 실행 시 자동으로 붙이고 경고를 띄움. 실행 중에 붙은 컴포넌트의 값은 종료하면 사라지므로,
    /// 값을 저장하려면 에디터에서 붙일 것 (YBotController 컴포넌트 우클릭 > Add Hand IK To Animator Object)
    /// </summary>
    private void EnsureHandIK()
    {
        if (animator == null || animator.TryGetComponent<YBotHandIK>(out _))
            return;

        animator.gameObject.AddComponent<YBotHandIK>();
        Debug.LogWarning(
            "Animator 오브젝트에 YBotHandIK가 없어 실행 중에 자동으로 붙였습니다. " +
            "값을 저장하려면 에디터에서 직접 붙이세요.", animator.gameObject);
    }

    /// <summary>
    /// 에디터용: Animator가 붙은 오브젝트에 YBotHandIK를 붙임 (컴포넌트 헤더 우클릭 메뉴)
    /// </summary>
    [ContextMenu("Add Hand IK To Animator Object")]
    private void AddHandIKInEditor()
    {
        Animator target = animator != null ? animator : GetComponentInChildren<Animator>();
        if (target == null)
        {
            Debug.LogError("Animator를 찾을 수 없습니다.", this);
            return;
        }

        if (target.TryGetComponent<YBotHandIK>(out _))
        {
            Debug.Log("이미 YBotHandIK가 있습니다.", target.gameObject);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.Undo.AddComponent<YBotHandIK>(target.gameObject);
        UnityEditor.EditorUtility.SetDirty(target.gameObject);
#else
        target.gameObject.AddComponent<YBotHandIK>();
#endif
        Debug.Log("YBotHandIK를 붙였습니다.", target.gameObject);
    }

    private void Update()
    {
        // 입력 (카메라 기준 이동)
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 cameraForward = Flatten(thirdPersonCamera != null
            ? thirdPersonCamera.Forward
            : transform.forward);
        Vector3 cameraRight = Flatten(thirdPersonCamera != null
            ? thirdPersonCamera.Right
            : transform.right);
        Vector3 inputDirection = Vector3.ClampMagnitude(
            cameraForward * vertical + cameraRight * horizontal,
            1.0f
        );
        bool hasMoveInput = inputDirection.sqrMagnitude > 0.01f;
        bool isRunning = hasMoveInput &&
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        bool jumpPressed = Input.GetKeyDown(jumpKey);
        bool sitPressed = Input.GetKeyDown(sitKey);

        // isGrounded는 직전 Move 결과이므로 프레임 시작 시 한 번만 읽음
        bool grounded = movement.IsGrounded;

        // 기능 간 조율: 점프, 앉기, 밀기는 서로 막음
        // 밀기는 이동 입력 방향의 물체를 감지해 시작하므로 가장 먼저 갱신
        push.Tick(grounded, inputDirection, hasMoveInput, jump.IsActive || sit.IsActive || climb.IsActive);

        // 등반은 점프로 공중에 떠 있을 때 앞의 벽을 감지해서 시작. 잡으면 점프는 중단하고 이동을 넘겨받음
        bool climbOwnsMovement = climb.Tick(
            inputDirection, hasMoveInput, jumpPressed,
            sit.IsActive || push.IsActive, jump.IsAirborne);
        if (climbOwnsMovement)
            jump.AbortForExternalState();

        // 점프 종류(제자리/걷기/달리기)는 YBotJump가 시작 순간의 이동 상태로 고름
        // 앉기는 이동 중이면 멈출 때까지 대기
        bool canStartJump = jumpPressed && grounded && !sit.IsActive && !push.IsActive && !climb.IsActive;
        jump.Tick(grounded, canStartJump);

        // 앉기 시작을 막는 조건 (파라미터 이름은 jumpActive지만 '시작 차단' 의미로 사용)
        sit.Tick(grounded, sitPressed, hasMoveInput, movement.IsIdle,
            jump.IsActive || push.IsActive || climb.IsActive);

        // 수평 이동 방식: 점프 중이면 점프가 정하고 (제자리 = 정지, 이동 점프 = 속도 유지),
        // 앉는 중이면 정지. 정지 모드에서는 속도가 감속되어 0으로 수렴
        YBotMoveMode moveMode = YBotMoveMode.Free;
        if (climb.IsActive)
            moveMode = YBotMoveMode.Stop;
        else if (jump.IsActive)
            moveMode = jump.HorizontalMode;
        else if (sit.IsActive)
            moveMode = YBotMoveMode.Stop;
        else if (push.IsActive)
        {
            moveMode = push.HorizontalMode;
            movement.SetPush(push.Direction, push.Speed);
        }

        // 벽에서 점프해 공중에 있는 동안에는 벽 점프 클립 기준 중력을 사용
        movement.Gravity = climbOwnsMovement
            ? 0.0f
            : climb.OverridesGravity ? climb.Gravity : jump.Gravity;
        if (climbOwnsMovement)
        {
            movement.StopHorizontal();
            movement.Launch(0.0f);
        }
        else
            movement.Tick(inputDirection, hasMoveInput, isRunning, grounded, moveMode);
    }

    /// <summary>
    /// 카메라 방향의 수직 성분을 제거해 수평 방향만 남김 (카메라가 내려다보고 있어도 이동 속도/캐릭터 기울기에 영향 없음)
    /// </summary>
    private static Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0.0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : direction;
    }
}