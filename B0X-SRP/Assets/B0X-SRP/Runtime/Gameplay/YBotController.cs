using UnityEngine;

/// <summary>
/// 입력을 읽고 Movement / Jump / Sit 기능을 조율하는 진입점
/// Update는 여기에만 있고, 각 기능은 Tick으로 호출되므로 실행 순서가 항상 고정
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(YBotMovement))]
[RequireComponent(typeof(YBotJump))]
[RequireComponent(typeof(YBotSit))]
[RequireComponent(typeof(YBotPush))]
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
    private ThirdPersonCamera thirdPersonCamera;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        thirdPersonCamera = FindAnyObjectByType<ThirdPersonCamera>();
        movement = GetOrAdd<YBotMovement>();
        jump = GetOrAdd<YBotJump>();
        sit = GetOrAdd<YBotSit>();
        push = GetOrAdd<YBotPush>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        movement.Initialize(characterController, animator);
        jump.Initialize(animator, movement);
        sit.Initialize(animator);
        push.Initialize(characterController, animator);
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

        // 장애물이 앞에 있으면 점프/앉기보다 밀기를 우선합니다.
        bool pushActive = push.Tick(grounded, hasMoveInput, inputDirection,
            jump.IsActive || sit.IsActive);

        // 기능 간 조율: 점프와 앉기는 서로 막음
        // 점프 종류(제자리/걷기/달리기)는 YBotJump가 시작 순간의 이동 상태로 고름
        // 앉기는 이동 중이면 멈출 때까지 대기
        bool canStartJump = jumpPressed && grounded && !sit.IsActive && !pushActive;
        jump.Tick(grounded, canStartJump);
        sit.Tick(grounded, sitPressed && !pushActive, hasMoveInput, movement.IsIdle,
            jump.IsActive || pushActive);

        // 수평 이동 방식: 점프 중이면 점프가 정하고 (제자리 = 정지, 이동 점프 = 속도 유지),
        // 앉는 중이면 정지. 정지 모드에서는 속도가 감속되어 0으로 수렴
        YBotMoveMode moveMode = YBotMoveMode.Free;
        if (jump.IsActive)
            moveMode = jump.HorizontalMode;
        else if (sit.IsActive)
            moveMode = YBotMoveMode.Stop;

        movement.Gravity = jump.Gravity;
        movement.Tick(
            inputDirection,
            hasMoveInput,
            isRunning && !pushActive,
            grounded,
            moveMode
        );
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
