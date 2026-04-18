// ════════════════════════════════════════════════════════════════════
//   PlayerController.cs  ──  Professional 3D Movement (Humanoid)
//   Features: Walk / Run / Jump / Gravity / Ground Detection / Animation
// ════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // ═══════════════════════════════════════════════
    //   INSPECTOR FIELDS
    // ═══════════════════════════════════════════════

    [Header("══ Movement Settings ══")]
    public float walkSpeed      = 4f;
    public float runSpeed       = 7f;
    public float rotationSpeed  = 12f;   // Speed at which character rotates toward movement

    [Header("══ Jump & Gravity ══")]
    public float jumpForce      = 10f;
    public float gravity        = -20f;
    public float coyoteTime     = 0.15f;  // Forgiveness time after leaving ground
    public float jumpBufferTime = 0.1f;   // Store jump input slightly before landing

    [Header("══ Ground Detection ══")]
    public LayerMask  groundLayer;
    public Transform  groundCheck;
    public float      groundCheckRadius = 0.25f;

    [Header("══ References ══")]
    public Animator   animator;
    public Transform  cameraTransform;   // Drag Hierarchy Camera here

    // ═══════════════════════════════════════════════
    //   PRIVATE STATE
    // ═══════════════════════════════════════════════

    private CharacterController cc;
    private Vector3  moveDirection;
    private float    velY;
    private bool     isGrounded;
    private bool     wasGroundedLastFrame;
    private float    coyoteTimer;
    private float    jumpBufTimer;
    private bool     isSprinting;
    private bool     isDead;

    // ── Animator Hashes (Faster than string lookups) ──
    private static readonly int H_SPEED    = Animator.StringToHash("Speed");
    private static readonly int H_GROUNDED = Animator.StringToHash("IsGrounded");
    private static readonly int H_JUMP     = Animator.StringToHash("JumpTrigger");
    private static readonly int H_DEAD     = Animator.StringToHash("IsDead");

    // ═══════════════════════════════════════════════
    //   INITIALIZATION
    // ═══════════════════════════════════════════════

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        
        // Ensure root motion doesn't interfere with manual movement
        animator.applyRootMotion = false;

        // Auto-assign Main Camera if reference is missing
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        // Create GroundCheck automatically if not assigned
        if (groundCheck == null)
        {
            var gc = new GameObject("_GroundCheck");
            gc.transform.SetParent(transform);
            gc.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            groundCheck = gc.transform;
        }
    }

    // ═══════════════════════════════════════════════
    //   CORE LOOP
    // ═══════════════════════════════════════════════

    void Update()
    {
        if (isDead) return;

        HandleGrounding();
        HandleInput();
        HandleJump();
        ApplyGravity();
        ApplyMovement();
        ApplyRotation();
        UpdateAnimations();
    }

    // ═══════════════════════════════════════════════
    //   GROUND CHECK
    // ═══════════════════════════════════════════════

    void HandleGrounding()
    {
        wasGroundedLastFrame = isGrounded;
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);

        if (isGrounded)
        {
            coyoteTimer = coyoteTime;
            if (velY < -2f) velY = -2f;   // Reset gravity accumulation
        }
        else
        {
            coyoteTimer -= Time.deltaTime;
        }
    }

    // ═══════════════════════════════════════════════
    //   INPUT → CAMERA-RELATIVE DIRECTION
    // ═══════════════════════════════════════════════

    void HandleInput()
    {
        float inputH = Input.GetAxisRaw("Horizontal");  // A / D or Left Stick X
        float inputV = Input.GetAxisRaw("Vertical");    // W / S or Left Stick Y
        isSprinting  = Input.GetKey(KeyCode.LeftShift);

        if (Input.GetButtonDown("Jump"))
            jumpBufTimer = jumpBufferTime;
        else
            jumpBufTimer -= Time.deltaTime;

        // Calculate direction relative to Camera orientation
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight   = cameraTransform.right;

        camForward.y = 0f;  // Ignore camera tilt
        camRight.y   = 0f;
        camForward.Normalize();
        camRight.Normalize();

        moveDirection = (camForward * inputV + camRight * inputH).normalized;
    }

    // ═══════════════════════════════════════════════
    //   JUMP
    // ═══════════════════════════════════════════════

    void HandleJump()
    {
        bool canJump = coyoteTimer > 0f;

        if (jumpBufTimer > 0f && canJump)
        {
            velY         = jumpForce;
            jumpBufTimer = 0f;
            coyoteTimer  = 0f;
            animator.SetTrigger(H_JUMP);
        }

        // Variable jump height: cut velocity if jump button is released early
        if (Input.GetButtonUp("Jump") && velY > 0f)
            velY *= 0.5f;
    }

    // ═══════════════════════════════════════════════
    //   GRAVITY
    // ═══════════════════════════════════════════════

    void ApplyGravity()
    {
        if (!isGrounded)
            velY += gravity * Time.deltaTime;
    }

    // ═══════════════════════════════════════════════
    //   MOVE
    // ═══════════════════════════════════════════════

    void ApplyMovement()
    {
        float speed = 0f;
        if (moveDirection.magnitude > 0.1f)
            speed = isSprinting ? runSpeed : walkSpeed;

        Vector3 motion = moveDirection * speed;
        motion.y = velY;

        cc.Move(motion * Time.deltaTime);
    }

    // ═══════════════════════════════════════════════
    //   ROTATION (Character faces movement direction)
    // ═══════════════════════════════════════════════

    void ApplyRotation()
    {
        if (moveDirection.magnitude < 0.1f) return;

        Quaternion targetRot = Quaternion.LookRotation(moveDirection);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            rotationSpeed * Time.deltaTime
        );
    }

    // ═══════════════════════════════════════════════
    //   ANIMATIONS
    // ═══════════════════════════════════════════════

    void UpdateAnimations()
    {
        float currentSpeed = 0f;
        if (moveDirection.magnitude > 0.1f)
            currentSpeed = isSprinting ? runSpeed : walkSpeed;

        animator.SetFloat(H_SPEED, currentSpeed, 0.1f, Time.deltaTime);
        animator.SetBool(H_GROUNDED, isGrounded);
    }

    // ═══════════════════════════════════════════════
    //   PUBLIC API
    // ═══════════════════════════════════════════════

    public void Die()
    {
        if (isDead) return;
        isDead = true;
        animator.SetBool(H_DEAD, true);
    }

    // ═══════════════════════════════════════════════
    //   DEBUG GIZMOS
    // ═══════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}