using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0.0f, 2.2f, -5.0f);
    [SerializeField] private float lookHeight = 1.2f;
    [SerializeField] private float followSmoothTime = 0.12f;
    [SerializeField] private float rotationSmoothSpeed = 12.0f;

    private Vector3 followVelocity;

    private void Awake()
    {
        if (target == null)
        {
            YBotController player = FindObjectOfType<YBotController>();
            if (player != null)
                target = player.transform;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desiredPosition = target.position + target.rotation * offset;
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
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                rotationSmoothSpeed * Time.deltaTime
            );
        }
    }
}
