using UnityEngine;

public sealed class OrbitalCameraFollow : MonoBehaviour
{
    [SerializeField] private string targetName = "Ship";
    [SerializeField] private Vector3 offset = new Vector3(0f, 8f, -15f);
    [SerializeField] private float followLerp = 5f;
    [SerializeField] private float lookLerp = 7f;

    private Transform _target;

    private void LateUpdate()
    {
        if (_target == null)
        {
            var go = GameObject.Find(targetName);
            if (go != null)
                _target = go.transform;
        }

        if (_target == null)
            return;

        var desiredPos = _target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPos, followLerp * Time.deltaTime);

        var desiredRot = Quaternion.LookRotation(_target.position - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, lookLerp * Time.deltaTime);
    }
}

