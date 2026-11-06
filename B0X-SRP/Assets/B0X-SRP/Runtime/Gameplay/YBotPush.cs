using UnityEngine;

/// <summary>
/// 앞의 낮은 Obstacle을 감지하고 Push 애니메이션 및 장애물 이동을 처리합니다.
/// </summary>
public class YBotPush : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] private string obstacleTag = "Obstacle";
    [SerializeField, Min(0.1f)] private float detectionDistance = 1.15f;
    [SerializeField, Min(0.01f)] private float detectionRadius = 0.18f;
    [SerializeField, Range(0.1f, 1.0f)] private float maximumObstacleHeightRatio = 0.9f;
    [SerializeField] private LayerMask obstacleLayers = Physics.DefaultRaycastLayers;

    [Header("Push")]
    [SerializeField, Min(0.0f)] private float pushSpeed = 3.0f;
    [SerializeField] private string pushParameter = "Push";

    private static readonly int PushHash = Animator.StringToHash("Push");

    private CharacterController characterController;
    private Animator animator;
    private Collider targetCollider;
    private Rigidbody targetBody;
    private bool isActive;

    public bool IsActive => isActive;

    public void Initialize(CharacterController controller, Animator targetAnimator)
    {
        characterController = controller;
        animator = targetAnimator;

        YBotAnimatorUtility.ValidateParameter(
            animator, pushParameter, AnimatorControllerParameterType.Bool, this);
    }

    /// <summary>
    /// 프레임당 한 번 호출합니다. 이동 입력 방향으로 낮은 장애물을 밀면 true를 반환합니다.
    /// </summary>
    public bool Tick(bool grounded, bool hasMoveInput, Vector3 inputDirection, bool blocked)
    {
        RaycastHit hit = default;
        bool canPush = grounded && hasMoveInput && !blocked &&
            Vector3.Dot(transform.forward, inputDirection.normalized) > 0.65f &&
            FindPushableObstacle(out hit);

        isActive = canPush;
        if (canPush)
        {
            targetCollider = hit.collider;
            targetBody = targetCollider.attachedRigidbody;

            Vector3 pushDirection = Vector3.ProjectOnPlane(inputDirection, Vector3.up).normalized;
            Vector3 displacement = pushDirection * pushSpeed * Time.deltaTime;
            if (targetBody != null && !targetBody.isKinematic)
                targetBody.MovePosition(targetBody.position + displacement);
            else
                targetCollider.transform.position += displacement;
        }
        else
        {
            targetCollider = null;
            targetBody = null;
        }

        if (animator != null)
            animator.SetBool(PushHash, isActive);

        return isActive;
    }

    private bool FindPushableObstacle(out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up * Mathf.Min(0.4f, characterController.height * 0.25f);
        Vector3 direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        if (!Physics.SphereCast(origin, detectionRadius, direction, out hit,
                detectionDistance, obstacleLayers, QueryTriggerInteraction.Ignore))
            return false;

        Collider obstacle = hit.collider;
        if (!obstacle.CompareTag(obstacleTag))
            return false;

        float characterHeight = characterController.height;
        float obstacleHeight = obstacle.bounds.size.y;
        float obstacleTopFromFeet = obstacle.bounds.max.y - transform.position.y;
        return obstacleHeight <= characterHeight * maximumObstacleHeightRatio &&
            obstacleTopFromFeet <= characterHeight * maximumObstacleHeightRatio;
    }
}
