using UnityEngine;

public sealed class OrbitalCameraFollow : MonoBehaviour
{
    [SerializeField] private string targetName = "Ship";
    [SerializeField] private Vector3 initialOffset = new Vector3(0f, 8f, -15f);
    [SerializeField] private float followLerp = 8f;
    [SerializeField] private float maxFollowSpeed = 80f;
    [SerializeField] private float maxFollowAcceleration = 120f;
    [SerializeField] private float lookLerp = 10f;
    [SerializeField] private float orbitSensitivity = 3f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 80f;
    [SerializeField] private KeyCode orbitMouseButton = KeyCode.Mouse1; // Right mouse button
    [SerializeField] private float zoomSensitivity = 2f;
    [SerializeField] private float minDistance = 5f;
    [SerializeField] private float maxDistance = 35f;

    private Transform _target;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private bool _initialized;
    private Vector3 _followVelocity;

    private void TryInitializeOrbit()
    {
        if (_initialized)
            return;

        var offset = initialOffset;
        _distance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);

        if (_distance < 0.001f)
        {
            _distance = 15f;
            _yaw = 0f;
            _pitch = 25f;
            _initialized = true;
            return;
        }

        var horizontal = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
        _yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        _pitch = Mathf.Atan2(offset.y, horizontal) * Mathf.Rad2Deg;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        _initialized = true;
    }

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

        TryInitializeOrbit();

        if (Input.GetKey(orbitMouseButton))
        {
            _yaw += Input.GetAxis("Mouse X") * orbitSensitivity;
            _pitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        var scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > Mathf.Epsilon)
        {
            _distance -= scroll * zoomSensitivity * 8f;
            _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
        }

        var orbitRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        var orbitOffset = orbitRotation * new Vector3(0f, 0f, -_distance);
        var desiredPos = _target.position + orbitOffset;
        var dt = Mathf.Max(0f, Time.deltaTime);
        if (dt > 0f)
        {
            // Acceleration-limited camera follow to reduce sharp jitter transfer from target corrections.
            var desiredVelocity = (desiredPos - transform.position) * followLerp;
            if (desiredVelocity.sqrMagnitude > maxFollowSpeed * maxFollowSpeed)
                desiredVelocity = desiredVelocity.normalized * maxFollowSpeed;

            var maxDeltaV = maxFollowAcceleration * dt;
            _followVelocity = Vector3.MoveTowards(_followVelocity, desiredVelocity, maxDeltaV);
            transform.position += _followVelocity * dt;
        }

        var desiredRot = Quaternion.LookRotation(_target.position - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, lookLerp * Time.deltaTime);
    }
}

