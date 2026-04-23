// ════════════════════════════════════════════════════════════════════
//   PlayerController.cs  —  نظام حركة احترافي مع Blend Tree
//   المتطلبات: Walk / Run / Jump / Fall / Ground Detection / Animation
// ════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    //   INSPECTOR FIELDS — المتغيرات التي تظهر في الـ Inspector
    // ══════════════════════════════════════════════════════

    [Header("══ إعدادات الحركة ══")]
    public float walkSpeed     = 3f;   // سرعة المشي
    public float runSpeed      = 6f;   // سرعة الجري
    public float rotationSpeed = 10f;  // سرعة دوران الشخصية نحو اتجاه الحركة

    [Header("══ القفز والجاذبية ══")]
    public float jumpHeight    = 1.5f; // ارتفاع القفزة بالمتر (أوضح من القوة المباشرة)
    public float gravity       = -15f; // قيمة الجاذبية (سالبة دائماً)
    public float coyoteTime    = 0.15f;// وقت السماح بالقفز بعد ترك الأرض (Coyote Time)
    public float jumpBuffer    = 0.12f;// حفظ ضغطة القفز قبل الهبوط (Jump Buffer)

    [Header("══ كشف الأرض ══")]
    public LayerMask groundLayer;          // طبقة الأرض في الـ Layer settings
    public Transform groundCheck;          // نقطة فحص الأرض (تحت القدمين)
    public float     groundRadius = 0.28f; // نصف قطر دائرة الفحص

    [Header("══ المراجع ══")]
    public Animator  animator;        // مكوّن الـ Animator على الشخصية
    public Transform cameraTransform; // ترانسفورم الكاميرا الرئيسية

    // ══════════════════════════════════════════════════════
    //   PRIVATE VARIABLES — المتغيرات الداخلية
    // ══════════════════════════════════════════════════════

    private CharacterController cc;   // مكوّن تحريك الشخصية
    private Vector3 moveDirection;    // اتجاه الحركة بعد حساب الكاميرا
    private float   velocityY;       // السرعة الرأسية (للقفز والجاذبية)
    private bool    isGrounded;       // هل الشخصية على الأرض؟
    private float   coyoteTimer;      // عداد وقت الـ Coyote
    private float   jumpBufferTimer;  // عداد تخزين ضغطة القفز
    private bool    isSprinting;      // هل الشخصية تجري؟
    private bool    isDead;           // هل الشخصية ماتت؟

    // ══ Animator Parameter Hashes ══
    // نستخدم الـ Hash بدل الـ String لأنه أسرع في المعالجة
    // هذه الأسماء يجب أن تطابق تماماً أسماء Parameters في الـ Animator
    private static readonly int HASH_SPEED     = Animator.StringToHash("Speed");
    private static readonly int HASH_GROUNDED  = Animator.StringToHash("IsGrounded");
    private static readonly int HASH_JUMP      = Animator.StringToHash("JumpTrigger");
    private static readonly int HASH_FALLING   = Animator.StringToHash("IsFalling");
    private static readonly int HASH_DEAD      = Animator.StringToHash("IsDead");

    // ══════════════════════════════════════════════════════
    //   AWAKE — يُنفَّذ مرة واحدة عند بدء اللعبة
    // ══════════════════════════════════════════════════════

    void Awake()
    {
        // جلب مكوّن CharacterController من نفس الـ GameObject
        cc = GetComponent<CharacterController>();

        // البحث عن Animator تلقائياً إذا لم يُسنَد في الـ Inspector
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // تعطيل Root Motion حتى لا تتعارض مع حركتنا اليدوية
        if (animator != null)
            animator.applyRootMotion = false;

        // تعيين كاميرا المشهد الرئيسية تلقائياً إذا لم تُسنَد
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        // إنشاء نقطة فحص الأرض تلقائياً إذا لم تُسنَد
        if (groundCheck == null)
        {
            GameObject gc = new GameObject("_GroundCheck");
            gc.transform.SetParent(transform);
            // نضعها عند القدمين مباشرة (أسفل بقليل من المركز)
            gc.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            groundCheck = gc.transform;
        }
    }

    // ══════════════════════════════════════════════════════
    //   UPDATE — يُنفَّذ كل فريم
    // ══════════════════════════════════════════════════════

    void Update()
    {
        // إذا ماتت الشخصية، نوقف كل شيء
        if (isDead) return;

        HandleGrounding();   // 1. فحص الأرض أولاً
        HandleInput();        // 2. قراءة الإدخال
        HandleJump();         // 3. معالجة القفز
        ApplyGravity();       // 4. تطبيق الجاذبية
        ApplyMovement();      // 5. تحريك الشخصية
        ApplyRotation();      // 6. دوران الشخصية
        UpdateAnimations();   // 7. تحديث الأنيميشن آخراً
    }

    // ══════════════════════════════════════════════════════
    //   HANDLE GROUNDING — فحص ما إذا كانت الشخصية على الأرض
    // ══════════════════════════════════════════════════════

    void HandleGrounding()
    {
        // CheckSphere: يرسم كرة خيالية عند groundCheck
        // إذا لامست الكرة أي Collider على طبقة groundLayer → نحن على الأرض
        isGrounded = Physics.CheckSphere(
            groundCheck.position,
            groundRadius,
            groundLayer,
            QueryTriggerInteraction.Ignore // نتجاهل Trigger Colliders
        );

        if (isGrounded)
        {
            // عند لمس الأرض: نعيد تعبئة Coyote Timer
            coyoteTimer = coyoteTime;

            // منع تراكم الجاذبية السلبية (يسبب مشكلة السقوط الغريب)
            // نضعها سالبة صغيرة بدل الصفر لضمان ضغط الشخصية على الأرض
            if (velocityY < -2f)
                velocityY = -2f;
        }
        else
        {
            // خارج الأرض: نبدأ عد تنازلي للـ Coyote Time
            coyoteTimer -= Time.deltaTime;
        }
    }

    // ══════════════════════════════════════════════════════
    //   HANDLE INPUT — قراءة إدخال اللاعب
    // ══════════════════════════════════════════════════════

    void HandleInput()
    {
        // GetAxisRaw: يعطي قيمة مباشرة (-1, 0, 1) بدون Smoothing
        float inputH = Input.GetAxisRaw("Horizontal"); // A/D أو الستيك
        float inputV = Input.GetAxisRaw("Vertical");   // W/S أو الستيك

        // Shift للجري
        isSprinting = Input.GetKey(KeyCode.LeftShift) && inputV > 0;

        // تخزين ضغطة القفز (Jump Buffer)
        if (Input.GetButtonDown("Jump"))
            jumpBufferTimer = jumpBuffer;
        else
            jumpBufferTimer -= Time.deltaTime;

        // ══ حساب الاتجاه نسبةً للكاميرا ══
        // هذا يجعل W دائماً "أمام الكاميرا" وليس أمام الشخصية
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight   = cameraTransform.right;

        // نزيل المكوّن الرأسي حتى لا تتحرك الشخصية للأعلى/الأسفل
        camForward.y = 0f;
        camRight.y   = 0f;

        // نُطبّع الاتجاهات (نجعل طولها = 1)
        camForward.Normalize();
        camRight.Normalize();

        // الاتجاه النهائي = (أمام × إدخال عمودي) + (يمين × إدخال أفقي)
        moveDirection = (camForward * inputV + camRight * inputH);

        // نُطبّع فقط إذا كان الطول > 1 (لمنع التسارع في الحركة القطرية)
        if (moveDirection.magnitude > 1f)
            moveDirection.Normalize();
    }

    // ══════════════════════════════════════════════════════
    //   HANDLE JUMP — معالجة القفز
    // ══════════════════════════════════════════════════════

    void HandleJump()
    {
        // يمكن القفز إذا: عداد القفز > 0 وعداد Coyote > 0
        bool canJump = coyoteTimer > 0f;

        if (jumpBufferTimer > 0f && canJump)
        {
            // حساب سرعة القفز من الارتفاع المطلوب:
            // v = sqrt(2 × |gravity| × height)
            // هذه المعادلة الفيزيائية تعطي ارتفاعاً محدداً بالمتر
            velocityY    = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);

            jumpBufferTimer = 0f; // مسح تخزين القفز
            coyoteTimer     = 0f; // منع القفز المزدوج

            // تشغيل Trigger الأنيميشن
            animator.SetTrigger(HASH_JUMP);
        }

        // ══ Variable Jump Height ══
        // إذا أفلت الزر مبكراً → نقلل السرعة الصاعدة → قفزة أقصر
        if (Input.GetButtonUp("Jump") && velocityY > 0f)
            velocityY *= 0.45f;
    }

    // ══════════════════════════════════════════════════════
    //   APPLY GRAVITY — تطبيق الجاذبية
    // ══════════════════════════════════════════════════════

    void ApplyGravity()
    {
        // نطبق الجاذبية فقط خارج الأرض
        if (!isGrounded)
        {
            // v = v₀ + g × t  (قانون الجاذبية التدريجي)
            velocityY += gravity * Time.deltaTime;

            // تحديد حد أقصى للسرعة السقوطية (Terminal Velocity)
            // منع السقوط بسرعة لانهائية
            velocityY = Mathf.Max(velocityY, -20f);
        }
    }

    // ══════════════════════════════════════════════════════
    //   APPLY MOVEMENT — تحريك الشخصية فعلياً
    // ══════════════════════════════════════════════════════

    void ApplyMovement()
    {
        // تحديد السرعة الأفقية
        float speed = 0f;
        if (moveDirection.magnitude > 0.05f)
            speed = isSprinting ? runSpeed : walkSpeed;

        // بناء متجه الحركة الكامل (أفقي + رأسي)
        Vector3 motion = moveDirection * speed;
        motion.y = velocityY; // إضافة الحركة الرأسية (قفز/سقوط)

        // cc.Move: يحرك الشخصية مع احترام الـ Colliders
        // نضرب في Time.deltaTime لاستقلالية الحركة عن الـ Frame Rate
        cc.Move(motion * Time.deltaTime);
    }

    // ══════════════════════════════════════════════════════
    //   APPLY ROTATION — تدوير الشخصية نحو اتجاه الحركة
    // ══════════════════════════════════════════════════════

    void ApplyRotation()
    {
        // لا ندور إذا لم تكن هناك حركة
        if (moveDirection.magnitude < 0.05f) return;

        // نحسب الزاوية المطلوبة من اتجاه الحركة
        Quaternion targetRotation = Quaternion.LookRotation(moveDirection);

        // Slerp: تدوير سلس من الزاوية الحالية للمطلوبة
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    // ══════════════════════════════════════════════════════
    //   UPDATE ANIMATIONS — تحديث معاملات الأنيميشن
    // ══════════════════════════════════════════════════════

    void UpdateAnimations()
    {
        // ══ حساب السرعة للـ Blend Tree ══
        // نمرر القيمة الفعلية للسرعة (0 = Idle, 3 = Walk, 6 = Run)
        float targetSpeed = 0f;
        if (moveDirection.magnitude > 0.05f)
            targetSpeed = isSprinting ? runSpeed : walkSpeed;

        // SetFloat مع Damping: يجعل التحول بين الأنيميشنات سلساً
        // 0.1f = وقت التحول بالثواني
        animator.SetFloat(HASH_SPEED, targetSpeed, 0.1f, Time.deltaTime);

        // ══ تحديث حالة الأرض ══
        animator.SetBool(HASH_GROUNDED, isGrounded);

        // ══ كشف السقوط ══
        // نعتبر الشخصية ساقطة إذا:
        // - ليست على الأرض
        // - وسرعتها الرأسية سلبية (تنزل للأسفل)
        // - وانتهى وقت الـ Coyote (لا نُشغّل Falling أثناء ذروة القفز)
        bool isFalling = !isGrounded && velocityY < -1f && coyoteTimer <= 0f;
        animator.SetBool(HASH_FALLING, isFalling);
    }

    // ══════════════════════════════════════════════════════
    //   PUBLIC API — دوال يمكن استدعاؤها من سكريبتات أخرى
    // ══════════════════════════════════════════════════════

    public void Die()
    {
        if (isDead) return;
        isDead = true;
        animator.SetBool(HASH_DEAD, true);
    }

    public void Respawn()
    {
        isDead    = false;
        velocityY = 0f;
        animator.SetBool(HASH_DEAD, false);
    }

    // ══════════════════════════════════════════════════════
    //   GIZMOS — رسم مساعد في الـ Scene View للتصحيح
    // ══════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;

        // أخضر = على الأرض / أحمر = في الهواء
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundRadius);
    }
}