using UnityEngine;

public class MockGazeProvider : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Editorテスト用カメラ。未設定なら Camera.main を使います。")]
    public Camera mockCamera;

    [Tooltip("頭部方向として使うTransform。未設定なら mockCamera.transform を使います。")]
    public Transform mockHeadTransform;

    [Header("Gaze Control")]
    [Tooltip("trueならマウス位置に向かって視線レイを飛ばします。")]
    public bool useMouseScreenPointRay = true;

    [Tooltip("falseの場合、マウス右ドラッグ/矢印キーで頭部方向からの視線オフセットを操作します。")]
    public bool useOffsetModeWhenMouseRayOff = true;

    public float mouseSensitivity = 2.0f;
    public float keyboardYawSpeed = 80.0f;
    public float keyboardPitchSpeed = 80.0f;

    [Range(-89f, 89f)]
    public float pitchOffsetDeg = 0f;

    public float yawOffsetDeg = 0f;

    public KeyCode resetOffsetKey = KeyCode.Space;

    [Header("Mock Tracking")]
    [Range(0f, 1f)]
    public float mockConfidence = 1.0f;

    public bool mockTrackingValid = true;

    [Header("Debug")]
    public bool drawDebugRay = true;
    public float debugRayLength = 5f;

    private void Awake()
    {
        if (mockCamera == null)
        {
            mockCamera = Camera.main;
        }

        if (mockHeadTransform == null && mockCamera != null)
        {
            mockHeadTransform = mockCamera.transform;
        }
    }

    private void Update()
    {
        if (useMouseScreenPointRay)
        {
            return;
        }

        if (!useOffsetModeWhenMouseRayOff)
        {
            return;
        }

        float dt = Time.unscaledDeltaTime;

        if (Input.GetKey(KeyCode.LeftArrow))
        {
            yawOffsetDeg -= keyboardYawSpeed * dt;
        }

        if (Input.GetKey(KeyCode.RightArrow))
        {
            yawOffsetDeg += keyboardYawSpeed * dt;
        }

        if (Input.GetKey(KeyCode.UpArrow))
        {
            pitchOffsetDeg -= keyboardPitchSpeed * dt;
        }

        if (Input.GetKey(KeyCode.DownArrow))
        {
            pitchOffsetDeg += keyboardPitchSpeed * dt;
        }

        if (Input.GetMouseButton(1))
        {
            yawOffsetDeg += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitchOffsetDeg -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        }

        pitchOffsetDeg = Mathf.Clamp(pitchOffsetDeg, -89f, 89f);

        if (Input.GetKeyDown(resetOffsetKey))
        {
            yawOffsetDeg = 0f;
            pitchOffsetDeg = 0f;
        }
    }

    public bool TryGetGaze(
        out Vector3 gazeOrigin,
        out Vector3 gazeDirection,
        out Vector3 headDirection,
        out float confidence,
        out bool trackingValid)
    {
        EnsureReferences();

        gazeOrigin = Vector3.zero;
        gazeDirection = Vector3.forward;
        headDirection = Vector3.forward;
        confidence = mockConfidence;
        trackingValid = mockTrackingValid;

        Transform head = mockHeadTransform;

        if (head == null)
        {
            Debug.LogWarning("MockGazeProvider: mockHeadTransform が見つかりません。");
            return false;
        }

        headDirection = head.forward.normalized;

        if (useMouseScreenPointRay)
        {
            if (mockCamera == null)
            {
                Debug.LogWarning("MockGazeProvider: mockCamera が見つかりません。");
                return false;
            }

            Ray ray = mockCamera.ScreenPointToRay(Input.mousePosition);
            gazeOrigin = ray.origin;
            gazeDirection = ray.direction.normalized;
        }
        else
        {
            gazeOrigin = head.position;

            Quaternion yaw = Quaternion.AngleAxis(yawOffsetDeg, Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(pitchOffsetDeg, head.right);

            gazeDirection = yaw * pitch * head.forward;
            gazeDirection.Normalize();
        }

        if (drawDebugRay)
        {
            Debug.DrawRay(gazeOrigin, gazeDirection * debugRayLength, Color.cyan);
            Debug.DrawRay(gazeOrigin, headDirection * debugRayLength, Color.yellow);
        }

        return true;
    }

    private void EnsureReferences()
    {
        if (mockCamera == null)
        {
            mockCamera = Camera.main;
        }

        if (mockHeadTransform == null && mockCamera != null)
        {
            mockHeadTransform = mockCamera.transform;
        }
    }
}