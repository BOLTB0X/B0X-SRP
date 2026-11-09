using UnityEngine;

/// <summary>
/// 수평 이동을 어떻게 처리할지
/// </summary>
public enum YBotMoveMode
{
    Free,   // 입력에 따라 가감속하고 회전
    Stop,   // 입력을 무시하고 0으로 감속 (제자리 점프/앉기 중)
    Hold,   // 현재 속도와 방향을 그대로 유지 (걷기/달리기 점프 중)
    Brake,  // 진행 방향은 유지하고 착지 감속
    Push    // 지정한 밀기 방향으로 몸을 돌리고 지정 속도로 이동 (YBotPush)
}

/// <summary>
/// 현재 수평 속도 기준 이동 상태
/// </summary>
public enum YBotLocomotion
{
    Idle,
    Walking,
    Running
}

/// <summary>
/// 수평 이동(가감속, 회전), 중력, CharacterController 이동, Speed 파라미터 갱신 담당
/// 입력을 직접 읽지 않고, YBotController가 넘겨주는 값만 사용
/// </summary>
public class YBotMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3.0f;
    [SerializeField] private float runSpeed = 6.0f;
    [SerializeField] private float acceleration = 20.0f;
    [SerializeField] private float deceleration = 25.0f;
    [SerializeField] private float landingDeceleration = 15.0f;
    [SerializeField] private float turnSpeed = 540.0f;
    [SerializeField] private float groundedGravity = -3.0f;
    [Tooltip("이 속도 이하면 정지 상태로 취급 (점프/앉기 시작 조건)")]
    [SerializeField] private float idleSpeedThreshold = 0.1f;
    [SerializeField] private float positionOffset = 0.08f;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    private CharacterController characterController;
    private Animator animator;

    private Vector3 moveDirection = Vector3.forward;
    private float currentSpeed;
    private float verticalVelocity;

    // 밀기 중에 YBotController가 넘겨주는 방향/속도
    private Vector3 pushDirection = Vector3.forward;
    private float pushSpeed;

    /// <summary>
    /// 중력 가속도 (음수). 점프 클립 기준으로 계산된 값을 YBotController가 넘겨줌
    /// </summary>
    public float Gravity { get; set; } = -9.0f;

    public float VerticalVelocity => verticalVelocity;
    public bool IsGrounded => characterController.isGrounded;
    public bool IsIdle => currentSpeed <= idleSpeedThreshold;

    /// <summary>
    /// 현재 수평 속도 기준 이동 상태 (정지 / 걷기 / 달리기)
    /// 걷기와 달리기는 두 속도의 중간값으로 나눔. 점프 종류를 고르는 데 사용
    /// </summary>
    public YBotLocomotion Locomotion
    {
        get
        {
            if (IsIdle)
                return YBotLocomotion.Idle;

            float runThreshold = (walkSpeed + runSpeed) * 0.5f;
            return currentSpeed >= runThreshold
                ? YBotLocomotion.Running
                : YBotLocomotion.Walking;
        }
    }

    public void Initialize(CharacterController controller, Animator targetAnimator)
    {
        characterController = controller;
        animator = targetAnimator;

        // 캐릭터 피벗이 발 위치이므로 캡슐 중심을 높이의 절반으로
        Vector3 center = characterController.center;
        center.y = characterController.height * 0.5f + positionOffset;
        characterController.center = center;
    }

    /// <summary>
    /// 수직 속도를 즉시 지정 (점프 이륙)
    /// </summary>
    public void Launch(float velocity)
    {
        verticalVelocity = velocity;
    }

    /// <summary>
    /// 밀기 방향과 속도를 지정 (Push 모드일 때 사용). 속도 0이면 제자리에서 방향만 맞춤
    /// </summary>
    public void SetPush(Vector3 direction, float speed)
    {
        pushDirection = direction;
        pushSpeed = speed;
    }

    /// <summary>
    /// 프레임당 한 번 호출. 수평 가감속 -> 수직 속도 -> CharacterController 이동 -> 애니메이터 갱신 순서
    /// </summary>
    /// <param name="hasMoveInput">이동 입력이 있는지</param>
    /// <param name="mode">수평 이동 처리 방식 (Free / Stop / Hold / Brake / Push)</param>
    public void Tick(
        Vector3      inputDirection,
        bool         hasMoveInput,
        bool         isRunning,
        bool         grounded,
        YBotMoveMode mode)
    {
        UpdateHorizontal(inputDirection, hasMoveInput, isRunning, mode);
        UpdateVertical(grounded);

        Vector3 velocity = moveDirection * currentSpeed;
        velocity.y = verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);

        UpdateAnimator();
    }

    /// <summary>
    /// 수평 속도 가감속과 회전. 멈출 때도 마지막 방향으로 감속하며 미끄러짐
    /// Hold 모드에서는 속도와 방향을 건드리지 않음 (걷기/달리기 점프의 공중 모멘텀)
    /// </summary>
    private void UpdateHorizontal(
        Vector3      inputDirection,
        bool         hasMoveInput,
        bool         isRunning,
        YBotMoveMode mode)
    {
        if (mode == YBotMoveMode.Hold)
            return;

        if (mode == YBotMoveMode.Brake)
        {
            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                0.0f,
                landingDeceleration * Time.deltaTime
            );
            return;
        }

        // 밀기: 물체 쪽으로 몸을 돌리고, 정렬된 뒤에 지정 속도로 이동
        if (mode == YBotMoveMode.Push)
        {
            moveDirection = pushDirection;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(pushDirection),
                turnSpeed * Time.deltaTime
            );

            // 속도 0이 넘어오면(밀기 시작/멈춤 구간) 감속 없이 즉시 정지
            if (pushSpeed <= 0.0f)
            {
                currentSpeed = 0.0f;
                return;
            }

            bool aligned = Vector3.Angle(transform.forward, pushDirection) < 15.0f;
            float pushTarget = aligned ? pushSpeed : 0.0f;
            float pushRate = pushTarget > currentSpeed ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, pushTarget, pushRate * Time.deltaTime);
            return;
        }

        bool wantsMove = hasMoveInput && mode == YBotMoveMode.Free;

        if (wantsMove)
        {
            moveDirection = inputDirection.normalized;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(moveDirection),
                turnSpeed * Time.deltaTime
            );
        }

        float targetSpeed = !wantsMove ? 0.0f : isRunning ? runSpeed : walkSpeed;
        float rate = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);
    }

    private void UpdateVertical(bool grounded)
    {
        if (grounded && verticalVelocity < 0.0f)
            verticalVelocity = groundedGravity;
        else
            verticalVelocity += Gravity * Time.deltaTime;
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