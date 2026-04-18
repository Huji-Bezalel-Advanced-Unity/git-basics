// ════════════════════════════════════════════════════════════════════
//   GTACameraController.cs  ──  Professional GTA V-Style Camera
//   Features: Follow / Yaw+Pitch / Auto-Rotate / Collision / Zoom /
//            Shoulder Offset / Look-Ahead / FOV Spring
//   Unity 6  |  Compatible with PlayerController.cs
// ════════════════════════════════════════════════════════════════════

using UnityEngine;

public class CameraController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════
    //   INSPECTOR FIELDS
    // ═══════════════════════════════════════════════════════════════

    [Header("══ Target ══")]
    public Transform target;               // Drag the Player here
    public Vector3   targetOffset = new Vector3(0f, 1.6f, 0f); // Pivot point (e.g., head level)

    [Header("══ Distance & Zoom ══")]
    public float defaultDistance = 5f;
    public float minDistance     = 1.5f;
    public float maxDistance     = 10f;
    public float zoomSpeed       = 3f;
    public float zoomDamping     = 8f;     // Zoom smoothing speed

    [Header("══ Mouse / Stick Input ══")]
    public float mouseSensitivityX = 200f;
    public float mouseSensitivityY = 120f;
    public bool  invertY           = false;
    [Tooltip("Gamepad Right Stick X axis name")]
    public string gamepadAxisX     = "RightStickX";
    [Tooltip("Gamepad Right Stick Y axis name")]
    public string gamepadAxisY     = "RightStickY";
    public float  stickSensitivity = 120f;

    [Header("══ Pitch Limits ══")]
    public float pitchMin = -20f;
    public float pitchMax =  60f;

    [Header("══ Rotation Smoothing ══")]
    public float positionDamping = 10f;    // Position follow speed
    public float rotationDamping = 12f;    // Slerp rotation speed

    [Header("══ Auto-Rotate Behind Player ══")]
    public bool  autoRotate          = true;
    public float autoRotateDelay     = 1.5f;  // Seconds before auto-rotation starts
    public float autoRotateSpeed     = 90f;   // Degrees per second

    [Header("══ Shoulder Offset ══")]
    [Tooltip("Offset camera to the right for over-the-shoulder effect")]
    public float shoulderOffset       = 0.5f;
    public float shoulderOffsetDamping = 6f;

    [Header("══ Look-Ahead When Running ══")]
    public bool  lookAhead           = true;
    public float lookAheadAmount     = 1.2f;  // Meters forward when sprinting
    public float lookAheadThreshold  = 5f;    // Speed threshold to start look-ahead
    public float lookAheadDamping    = 4f;

    [Header("══ Collision ══")]
    public LayerMask collisionLayers;
    public float     collisionRadius  = 0.3f;   // SphereCast radius
    public float     collisionPadding = 0.1f;   // Safe distance from walls
    public float     collisionDamping = 15f;    // Distance transition speed

    [Header("══ FOV Spring (GTA-style) ══")]
    public float baseFOV     = 60f;
    public float sprintFOV   = 65f;     // Wider FOV when sprinting
    public float fovDamping  = 5f;

    [Header("══ Cursor ══")]
    public bool lockCursor = true;

    // ═══════════════════════════════════════════════════════════════
    //   PRIVATE STATE
    // ═══════════════════════════════════════════════════════════════

    private Camera   cam;
    private float    yaw;                  // Horizontal rotation
    private float    pitch;                // Vertical rotation
    private float    currentDistance;
    private float    desiredDistance;
    private float    autoRotateTimer;
    private float    currentShoulderOffset;
    private Vector3  currentLookAheadOffset;
    private Vector3  currentPosition;
    private Quaternion currentRotation;

    // Cache to track player velocity (needed for Look-Ahead)
    private Vector3  lastTargetPos;
    private Vector3  targetVelocity;

    // Helper: Check if there is any mouse movement
    private bool HasMouseInput =>
        Mathf.Abs(Input.GetAxis("Mouse X")) > 0.01f ||
        Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.01f;

    // Helper: Check if there is any controller stick movement
    private bool HasStickInput
    {
        get
        {
            try
            {
                return Mathf.Abs(Input.GetAxis(gamepadAxisX)) > 0.05f ||
                       Mathf.Abs(Input.GetAxis(gamepadAxisY)) > 0.05f;
            }
            catch { return false; }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //   INIT
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = GetComponentInChildren<Camera>();

        // Start directly behind the player
        if (target != null)
        {
            yaw   = target.eulerAngles.y;
            pitch = 15f;
        }

        currentDistance       = defaultDistance;
        desiredDistance       = defaultDistance;
        currentShoulderOffset = shoulderOffset;
        currentPosition       = transform.position;
        currentRotation       = transform.rotation;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //   LATE UPDATE — Calculated after movement
    // ═══════════════════════════════════════════════════════════════

    void LateUpdate()
    {
        if (target == null) return;

        HandleCursorToggle();
        ComputeTargetVelocity();
        HandleMouseInput();
        HandleZoom();
        HandleAutoRotate();
        HandleLookAhead();
        HandleShoulderOffset();
        ComputeFinalPosition();
        ApplyFOV();
    }

    // ═══════════════════════════════════════════════════════════════
    //   CURSOR TOGGLE (Tab to show/hide cursor)
    // ═══════════════════════════════════════════════════════════════

    void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible   = locked;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //   VELOCITY — Manual velocity calculation
    // ═══════════════════════════════════════════════════════════════

    void ComputeTargetVelocity()
    {
        if (Time.deltaTime < Mathf.Epsilon) return;
        targetVelocity = (target.position - lastTargetPos) / Time.deltaTime;
        lastTargetPos  = target.position;
    }

    // ═══════════════════════════════════════════════════════════════
    //   MOUSE / STICK INPUT
    // ═══════════════════════════════════════════════════════════════

    void HandleMouseInput()
    {
        float inputX = 0f, inputY = 0f;
        bool  hadInput = false;

        // ── Keyboard/Mouse Input ──
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            inputX += Input.GetAxis("Mouse X") * mouseSensitivityX * Time.deltaTime;
            inputY += Input.GetAxis("Mouse Y") * mouseSensitivityY * Time.deltaTime;
            if (HasMouseInput) hadInput = true;
        }

        // ── Gamepad Right Stick Input ──
        if (HasStickInput)
        {
            try
            {
                inputX += Input.GetAxis(gamepadAxisX) * stickSensitivity * Time.deltaTime;
                inputY += Input.GetAxis(gamepadAxisY) * stickSensitivity * Time.deltaTime;
            }
            catch { }
            hadInput = true;
        }

        // Apply rotation
        yaw   += inputX;
        pitch += invertY ? inputY : -inputY;
        pitch  = Mathf.Clamp(pitch, pitchMin, pitchMax);

        // Reset auto-rotate timer if any input is detected
        if (hadInput)
            autoRotateTimer = 0f;
    }

    // ═══════════════════════════════════════════════════════════════
    //   ZOOM
    // ═══════════════════════════════════════════════════════════════

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        desiredDistance -= scroll * zoomSpeed;
        desiredDistance  = Mathf.Clamp(desiredDistance, minDistance, maxDistance);
        // Note: Application is handled within the Collision logic
    }

    // ═══════════════════════════════════════════════════════════════
    //   AUTO-ROTATE BEHIND PLAYER
    // ═══════════════════════════════════════════════════════════════

    void HandleAutoRotate()
    {
        if (!autoRotate) return;

        bool isMoving = targetVelocity.sqrMagnitude > 0.5f;

        if (!isMoving || HasMouseInput || HasStickInput)
        {
            autoRotateTimer = 0f;
            return;
        }

        autoRotateTimer += Time.deltaTime;

        if (autoRotateTimer < autoRotateDelay) return;

        // Calculate target Yaw (directly behind player movement)
        float targetYaw = Mathf.Atan2(targetVelocity.x, targetVelocity.z) * Mathf.Rad2Deg;

        // Smoothly rotate current yaw towards target yaw (shortest path)
        float delta = Mathf.DeltaAngle(yaw, targetYaw);
        yaw += Mathf.Sign(delta) *
               Mathf.Min(Mathf.Abs(delta), autoRotateSpeed * Time.deltaTime);
    }

    // ═══════════════════════════════════════════════════════════════
    //   LOOK-AHEAD
    // ═══════════════════════════════════════════════════════════════

    void HandleLookAhead()
    {
        if (!lookAhead) return;

        Vector3 flatVel = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
        float   speed   = flatVel.magnitude;

        Vector3 desiredAhead = Vector3.zero;
        if (speed > lookAheadThreshold)
            desiredAhead = flatVel.normalized * lookAheadAmount;

        currentLookAheadOffset = Vector3.Lerp(
            currentLookAheadOffset,
            desiredAhead,
            lookAheadDamping * Time.deltaTime
        );
    }

    // ═══════════════════════════════════════════════════════════════
    //   SHOULDER OFFSET SMOOTH
    // ═══════════════════════════════════════════════════════════════

    void HandleShoulderOffset()
    {
        // Future use: Can be reversed when aiming
        currentShoulderOffset = Mathf.Lerp(
            currentShoulderOffset,
            shoulderOffset,
            shoulderOffsetDamping * Time.deltaTime
        );
    }

    // ═══════════════════════════════════════════════════════════════
    //   COMPUTE FINAL POSITION + COLLISION
    // ═══════════════════════════════════════════════════════════════

    void ComputeFinalPosition()
    {
        // ─── 1. Pivot Point ───
        Vector3 pivotPos = target.position + targetOffset + currentLookAheadOffset;

        // ─── 2. Camera Rotation ───
        Quaternion desiredRotation = Quaternion.Euler(pitch, yaw, 0f);

        // ─── 3. Desired Position (Before Collision) ───
        // Add shoulder offset to the right of the camera
        Vector3 rightOffset     = desiredRotation * Vector3.right * currentShoulderOffset;
        Vector3 rawDesiredPos   = pivotPos
                                + rightOffset
                                - desiredRotation * Vector3.forward * desiredDistance;

        // ─── 4. Collision Detection using SphereCast ───
        float   safeDistance = GetCollisionSafeDistance(pivotPos, desiredRotation, rightOffset);

        // Smooth distance transition
        currentDistance = Mathf.Lerp(
            currentDistance,
            safeDistance,
            collisionDamping * Time.deltaTime
        );

        Vector3 finalDesiredPos = pivotPos
                                + rightOffset
                                - desiredRotation * Vector3.forward * currentDistance;

        // ─── 5. Smooth Position + Rotation ───
        currentPosition = Vector3.Lerp(
            currentPosition,
            finalDesiredPos,
            positionDamping * Time.deltaTime
        );

        currentRotation = Quaternion.Slerp(
            currentRotation,
            desiredRotation,
            rotationDamping * Time.deltaTime
        );

        transform.position = currentPosition;
        transform.rotation = currentRotation;
    }

    // ═══════════════════════════════════════════════════════════════
    //   COLLISION SAFE DISTANCE
    // ═══════════════════════════════════════════════════════════════

    float GetCollisionSafeDistance(Vector3 pivot, Quaternion rot, Vector3 rightOffset)
    {
        Vector3 origin    = pivot + rightOffset;
        Vector3 direction = -(rot * Vector3.forward);  // From Pivot towards Camera

        if (Physics.SphereCast(
            origin,
            collisionRadius,
            direction,
            out RaycastHit hit,
            desiredDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore))
        {
            // Return safe distance without clipping through walls
            return Mathf.Max(hit.distance - collisionPadding, minDistance);
        }

        // No obstruction → return desiredDistance smoothly with zoom damping
        return Mathf.Lerp(currentDistance, desiredDistance, zoomDamping * Time.deltaTime);
    }

    // ═══════════════════════════════════════════════════════════════
    //   FOV SPRING
    // ═══════════════════════════════════════════════════════════════

    void ApplyFOV()
    {
        if (cam == null) return;

        float flatSpeed  = new Vector3(targetVelocity.x, 0f, targetVelocity.z).magnitude;
        float targetFOV  = flatSpeed > lookAheadThreshold ? sprintFOV : baseFOV;

        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, fovDamping * Time.deltaTime);
    }

    // ═══════════════════════════════════════════════════════════════
    //   PUBLIC API
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Toggle shoulder offset and FOV for aiming effect</summary>
    public void SetAiming(bool aiming)
    {
        shoulderOffset = aiming ? -0.5f : 0.5f;
        if (aiming) baseFOV = 45f;
        else        baseFOV = 60f;
    }

    /// <summary>Immediately snap the camera behind the player without smoothing</summary>
    public void SnapBehindPlayer()
    {
        if (target == null) return;
        yaw             = target.eulerAngles.y;
        currentDistance = desiredDistance;
        currentPosition = target.position + targetOffset
                        - Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * currentDistance;
        transform.position = currentPosition;
    }

    // ═══════════════════════════════════════════════════════════════
    //   GIZMOS
    // ═══════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (target == null) return;

        Vector3 pivot = target.position + targetOffset;

        // Draw Pivot Point
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pivot, 0.1f);

        // Draw line from Pivot to Camera
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivot, transform.position);

        // Draw Collision Radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, collisionRadius);
    }
}