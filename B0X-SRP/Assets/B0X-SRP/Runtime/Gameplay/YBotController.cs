using UnityEngine;

/// <summary>
/// 입력을 읽고 Movement / Jump / Sit 기능을 조율하는 진입점
/// Update는 여기에만 있고, 각 기능은 Tick으로 호출되므로 실행 순서가 항상 고정
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(YBotMovement))]
[RequireComponent(typeof(YBotJump))]
[RequireComponent(typeof(YBotSit))]
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

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        movement = GetOrAdd<YBotMovement>();
        jump = GetOrAdd<YBotJump>();
        sit = GetOrAdd<YBotSit>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        movement.Initialize(characterController, animator);
        jump.Initialize(animator, movement);
        sit.Initialize(animator);
    }

    /// <summary>
    /// 기존 오브젝트에 컴포넌트가 아직 없어도 동작하도록 보충
    /// 인스펙터에서 값을 튜닝하려면 에디터에서 직접 붙여두는 편이 좋다네
    /// </summary>
    private T GetOrAdd<T>() where T : Component
    {
        if (!TryGetComponent(out T component))
            component = gameObject.AddComponent<T>();

        return component;
    }

    private void Update()
    {
        // 입력
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 inputDirection = Vector3.ClampMagnitude(
            new Vector3(horizontal, 0.0f, vertical), 1.0f
        );
        bool hasMoveInput = inputDirection.sqrMagnitude > 0.01f;
        bool isRunning = hasMoveInput &&
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        bool jumpPressed = Input.GetKeyDown(jumpKey);
        bool sitPressed = Input.GetKeyDown(sitKey);

        // isGrounded는 직전 Move 결과이므로 프레임 시작 시 한 번만 읽음
        bool grounded = movement.IsGrounded;

        // 기능 간 조율: 점프는 앉기와 서로 막고,
        // 앉기는 이동 중에는 대기. 점프/앉기 중에는 이동 입력을 무시
        bool canStartJump = jumpPressed && grounded && movement.IsIdle && !sit.IsActive;
        jump.Tick(grounded, canStartJump);
        sit.Tick(grounded, sitPressed, hasMoveInput, movement.IsIdle, jump.IsActive);

        // 점프/앉기 중에는 수평 입력을 막음 (잠긴 동안 속도는 감속되어 0으로 수렴)
        bool movementLocked = jump.IsActive || sit.IsActive;
        movement.Gravity = jump.Gravity;
        movement.Tick(inputDirection, hasMoveInput && !movementLocked, isRunning, grounded);
    }
}