using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class YBotController_Backup : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.0f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundedGravity = -2.0f;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private CharacterController characterController;
    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        Vector3 center = characterController.center;
        center.y = characterController.height * 0.5f;
        characterController.center = center;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        // WASD 입력
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 inputDirection = new Vector3(
            horizontal,
            0.0f,
            vertical
        );

        // 대각선 이동 속도 보정
        inputDirection = Vector3.ClampMagnitude(inputDirection, 1.0f);

        // 월드 좌표 기준 이동
        Vector3 movement = inputDirection * moveSpeed;

        if (characterController.isGrounded && verticalVelocity < 0.0f)
            verticalVelocity = groundedGravity;
        else
            verticalVelocity += gravity * Time.deltaTime;

        movement.y = verticalVelocity;
        characterController.Move(movement * Time.deltaTime);

        // 이동 방향 바라보기
        if (inputDirection.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(inputDirection);
        }

        // Animator에 이동 속도 전달
        if (animator != null)
            animator.SetFloat("Speed", inputDirection.magnitude);
    }
}
