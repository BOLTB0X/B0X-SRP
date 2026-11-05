using UnityEngine;

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
    [SerializeField] private float turnSpeed = 540.0f;
    [SerializeField] private float groundedGravity = -2.0f;
    [Tooltip("이 속도 이하면 정지 상태로 취급 (점프/앉기 시작 조건)")]
    [SerializeField] private float idleSpeedThreshold = 0.1f;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    private CharacterController characterController;
    private Animator animator;

    private Vector3 moveDirection = Vector3.forward;
    private float currentSpeed;
    private float verticalVelocity;

    /// <summary>
    /// 중력 가속도 (음수). 점프 클립 기준으로 계산된 값을 YBotController가 넘겨줌
    /// </summary>
    public float Gravity { get; set; } = -9.81f;

    public float VerticalVelocity => verticalVelocity;
    public bool IsGrounded => characterController.isGrounded;
    public bool IsIdle => currentSpeed <= idleSpeedThreshold;

    public void Initialize(CharacterController controller, Animator targetAnimator)
    {
        characterController = controller;
        animator = targetAnimator;

        // 캐릭터 피벗이 발 위치이므로 캡슐 중심을 높이의 절반으로
        Vector3 center = characterController.center;
        center.y = characterController.height * 0.5f;
        characterController.center = center;
    }

    /// <summary>수
    /// 직 속도를 즉시 지정한다 (점프 이륙)
    /// </summary>
    public void Launch(float velocity)
    {
        verticalVelocity = velocity;
    }

    /// <summary>
    /// 프레임당 한 번 호출. 수평 가감속 -> 수직 속도 -> CharacterController 이동 -> 애니메이터 갱신 순서
    /// </summary>
    /// <param name="wantsMove">false면 입력을 무시하고 0으로 감속 (점프/앉기 중 잠금)</param>
    public void Tick(
        Vector3 inputDirection,
        bool    wantsMove,
        bool    isRunning,
        bool    grounded)
    {
        UpdateHorizontal(inputDirection, wantsMove, isRunning);
        UpdateVertical(grounded);

        Vector3 velocity = moveDirection * currentSpeed;
        velocity.y = verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);

        UpdateAnimator();
    }

    /// <summary>
    /// 수평 속도 가감속과 회전. 멈출 때도 마지막 방향으로 감속하며 미끄러짐
    /// </summary>
    private void UpdateHorizontal(
        Vector3 inputDirection, 
        bool    wantsMove,
        bool    isRunning)
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