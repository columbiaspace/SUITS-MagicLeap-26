using UnityEngine;

public class SmoothFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform followTarget;

    [Header("Offset (Local to Camera)")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, 0.15f, 1.5f);

    [Header("Smoothing")]
    [SerializeField] private float positionLerpSpeed = 3f;
    [SerializeField] private float rotationSlerpSpeed = 3f;

    private void Awake()
    {
        if (followTarget == null)
        {
            followTarget = Camera.main != null ? Camera.main.transform : null;
        }
    }

    private void LateUpdate()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector3 targetPosition = followTarget.TransformPoint(positionOffset);
        transform.position = Vector3.Lerp(transform.position, targetPosition, positionLerpSpeed * Time.deltaTime);

        Quaternion targetRotation = Quaternion.LookRotation(transform.position - followTarget.position, followTarget.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSlerpSpeed * Time.deltaTime);
    }
}
