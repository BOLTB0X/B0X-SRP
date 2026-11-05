using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0.0f, 2.2f, -5.0f);
    [SerializeField] private float lookHeight = 1.2f;
    [SerializeField] private float followSmoothTime = 0.12f;
    [SerializeField] private float rotationDeadZone = 50.0f;
    [SerializeField] private float rotationFollowSpeed = 120.0f;

    private Vector3 followVelocity;
    private float cameraYaw;

    private void Awake()
    {
        cameraYaw = transform.eulerAngles.y;

        if (target == null)
        {
            YBotController player = FindAnyObjectByType<YBotController>();
            if (player != null)
                target = player.transform;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        float targetYaw = target.eulerAngles.y;
        if (Mathf.Abs(Mathf.DeltaAngle(cameraYaw, targetYaw)) > rotationDeadZone)
        {
            cameraYaw = Mathf.MoveTowardsAngle(
                cameraYaw,
                targetYaw,
                rotationFollowSpeed * Time.deltaTime
            );
        }

        Quaternion cameraOrbitRotation = Quaternion.Euler(0.0f, cameraYaw, 0.0f);
        Vector3 desiredPosition = target.position + cameraOrbitRotation * offset;
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref followVelocity,
            followSmoothTime
        );

        Vector3 lookTarget = target.position + Vector3.up * lookHeight;
        Vector3 lookDirection = lookTarget - transform.position;
        if (lookDirection.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                desiredRotation,
                rotationFollowSpeed * Time.deltaTime
            );
        }
    }
}
