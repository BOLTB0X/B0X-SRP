using UnityEngine;

/// <summary>
/// 밀기 중 양손을 물체 표면에, 벽타기 중 양손을 벽/모서리에 맞추는 Humanoid 손 IK
/// 클립에 구워진 손 위치는 물체 크기/거리와 무관하게 고정이라 어긋나므로,
/// 손이 가야 할 표면 위치를 매 프레임 구해서 IK로 보정
/// </summary>
[RequireComponent(typeof(Animator))]
public class YBotHandIK : MonoBehaviour
{
    [Header("Contact")]
    [Tooltip("손목(IK 목표점)에서 손바닥 면까지의 거리(m). 손이 물체에 파묻히면 늘리고, 떠 있으면 줄임")]
    [SerializeField] private float handSurfaceOffset = 0.09f;
    [Tooltip("물체 위/아래 가장자리에서 손이 닿을 최소 여유 높이(m). 낮은 물체에서 손이 모서리 밖으로 안 나가게 함")]
    [SerializeField] private float heightMargin = 0.15f;
    [Tooltip("원래 손 위치에서 목표점까지 이 거리보다 멀면 닿을 수 없다고 보고 IK를 끔")]
    [SerializeField] private float maxReach = 0.6f;
    [Tooltip("원래 손 위치에서 이 거리만큼 뒤에서 표면 방향으로 광선을 쏨")]
    [SerializeField] private float rayBackDistance = 0.5f;

    [Header("Blend")]
    [Tooltip("IK 가중치가 0 <-> 1로 바뀌는 속도(초당)")]
    [SerializeField] private float blendSpeed = 6.0f;
    [Range(0.0f, 1.0f)]
    [SerializeField] private float positionWeight = 1.0f;

    [Header("Rotation (선택)")]
    [Tooltip("0이면 위치만 맞춤. 손바닥 방향까지 맞추려면 올리고 아래 오프셋으로 손 모양을 조정")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float rotationWeight = 0.0f;
    [SerializeField] private Vector3 leftRotationOffset;
    [SerializeField] private Vector3 rightRotationOffset;

    [Header("Wall Climb Feet")]
    [Tooltip("매달리기/등반 중 발이 벽에서 떨어질 때 벽 표면에 붙이는 위치 IK")]
    [SerializeField] private bool useWallFootIK = true;
    [Tooltip("발 목표점을 벽 표면에서 캐릭터 쪽으로 띄우는 거리(m)")]
    [SerializeField] private float footSurfaceOffset = 0.2f;
    [Tooltip("애니메이션 발 위치에서 벽 IK 목표점까지 허용하는 최대 거리(m)")]
    [SerializeField] private float footMaxReach = 0.65f;

    private class HandState
    {
        public float weight;
        public Vector3 position;                       // 마지막으로 구한 목표 위치 (가중치가 줄어드는 동안에도 사용)
        public Quaternion rotation = Quaternion.identity;
    }

    private readonly HandState left = new HandState();
    private readonly HandState right = new HandState();
    private readonly HandState leftFoot = new HandState();
    private readonly HandState rightFoot = new HandState();

    private Animator animator;
    private YBotPush push;
    private YBotClimb climb;

    // 진단용: OnAnimatorIK가 호출되는지 확인 (레이어의 IK Pass가 꺼져 있으면 호출되지 않음)
    private int lastIkFrame = -1;
    private float contactTime;
    private bool warnedIkPass;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        push = GetComponentInParent<YBotPush>();
        climb = GetComponentInParent<YBotClimb>();
    }

    private void Update()
    {
        bool isPushing = push != null && push.IsGripping;
        bool isClimbing = climb != null && climb.IsHandIKActive;
        if (!isPushing && !isClimbing)
        {
            contactTime = 0.0f;
            return;
        }

        contactTime += Time.deltaTime;
        if (!warnedIkPass && contactTime > 0.5f && Time.frameCount - lastIkFrame > 2)
        {
            warnedIkPass = true;
            Debug.LogWarning(
                "손 접촉 중인데 OnAnimatorIK가 호출되지 않습니다. Animator 창 Layers 탭에서 Base Layer 톱니바퀴의 'IK Pass'를 켜고, " +
                "이 컴포넌트가 Animator가 붙은 오브젝트에 있는지 확인하세요.", this);
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        lastIkFrame = Time.frameCount;

        if (animator == null)
            return;

        Collider surface = push != null && push.IsGripping ? push.TargetCollider : null;

        UpdateHand(AvatarIKGoal.LeftHand, left, surface, leftRotationOffset);
        UpdateHand(AvatarIKGoal.RightHand, right, surface, rightRotationOffset);
        UpdateFoot(AvatarIKGoal.LeftFoot, leftFoot);
        UpdateFoot(AvatarIKGoal.RightFoot, rightFoot);
    }

    private void UpdateHand(AvatarIKGoal goal, HandState hand, Collider surface, Vector3 rotationOffset)
    {
        float desiredWeight = 0.0f;
        bool isLedgeContact = false;

        if (surface != null)
        {
            Vector3 pushDirection = push.Direction;

            // 클립이 만든 원래 손 위치
            Vector3 handPosition = animator.GetIKPosition(goal);

            // 손 높이를 물체의 위/아래 범위 안으로 제한해서 작은 물체에서도 면 위에 닿게 함
            Bounds bounds = surface.bounds;
            float y = Mathf.Clamp(
                handPosition.y,
                bounds.min.y + heightMargin,
                bounds.max.y - heightMargin
            );

            // 손 뒤쪽에서 밀기 방향으로 광선을 쏴 표면 위치를 찾음 (이 콜라이더만 검사하므로 다른 물체에 안 걸림)
            Vector3 origin = new Vector3(handPosition.x, y, handPosition.z) - pushDirection * rayBackDistance;
            Ray ray = new Ray(origin, pushDirection);

            if (surface.Raycast(ray, out RaycastHit hit, rayBackDistance + maxReach))
            {
                Vector3 target = hit.point + hit.normal * handSurfaceOffset;

                if (Vector3.Distance(handPosition, target) <= maxReach)
                {
                    hand.position = target;
                    hand.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up) * Quaternion.Euler(rotationOffset);
                    desiredWeight = 1.0f;
                }
            }
        }
        else if (climb != null && climb.IsHandIKActive)
        {
            Vector3 handPosition = animator.GetIKPosition(goal);
            if (climb.TryGetHandIKTarget(
                    goal,
                    handPosition,
                    handSurfaceOffset,
                    out Vector3 target,
                    out Quaternion targetHandRotation,
                    out isLedgeContact) &&
                Vector3.Distance(handPosition, target) <= maxReach)
            {
                hand.position = target;
                hand.rotation = targetHandRotation * Quaternion.Euler(rotationOffset);
                desiredWeight = 1.0f;
            }
        }

        if (climb != null)
            climb.SetHandLedgeContact(goal, desiredWeight > 0.0f && isLedgeContact);

        hand.weight = Mathf.MoveTowards(hand.weight, desiredWeight, blendSpeed * Time.deltaTime);
        if (hand.weight <= 0.0f)
            return;

        animator.SetIKPositionWeight(goal, hand.weight * positionWeight);
        animator.SetIKPosition(goal, hand.position);

        if (rotationWeight > 0.0f)
        {
            animator.SetIKRotationWeight(goal, hand.weight * rotationWeight);
            animator.SetIKRotation(goal, hand.rotation);
        }
    }

    private void UpdateFoot(AvatarIKGoal goal, HandState foot)
    {
        float desiredWeight = 0.0f;
        if (useWallFootIK && climb != null && climb.IsHandIKActive)
        {
            Vector3 footPosition = animator.GetIKPosition(goal);
            if (climb.TryGetFootIKTarget(
                    footPosition,
                    footSurfaceOffset,
                    out Vector3 target,
                    out _) &&
                Vector3.Distance(footPosition, target) <= footMaxReach)
            {
                foot.position = target;
                desiredWeight = 1.0f;
            }
        }

        foot.weight = Mathf.MoveTowards(foot.weight, desiredWeight, blendSpeed * Time.deltaTime);
        animator.SetIKPositionWeight(goal, foot.weight);
        if (foot.weight <= 0.0f)
            return;

        animator.SetIKPosition(goal, foot.position);
    }

    /// <summary>
    /// 선택했을 때 실행 중이면 양손이 향하는 목표 지점을 보여줌 (파랑 = 왼손, 빨강 = 오른손)
    /// 이 점이 큐브 표면 바로 앞에 찍히는지 보면서 handSurfaceOffset을 조정
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        if (left.weight > 0.0f)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(left.position, 0.05f);
        }

        if (right.weight > 0.0f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(right.position, 0.05f);
        }
    }
}
