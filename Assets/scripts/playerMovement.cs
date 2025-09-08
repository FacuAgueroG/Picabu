using UnityEngine;
using System.Collections;
using System.Collections.Generic; // <<< NUEVO

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Player2DController : MonoBehaviour {
    [Header("Movement")]
    [SerializeField] private float baseSpeed = 6f;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float accel = 999f;
    [SerializeField] private bool faceRightDefault = true;
    [SerializeField] private float airDrag = 5f;

    [Header("Mario-style Gravity")]
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float riseGravityMultiplier = 1.0f;
    [SerializeField] private float fallRampTimeJump = 0.15f;
    [SerializeField] private float fallRampTimeDash = 0.15f;

    [Header("Jump (Press-to-Jump + Hold-to-Height)")]
    [SerializeField] private float jumpMinForce = 6f;
    [SerializeField] private float jumpMaxForce = 14f;
    [SerializeField] private float jumpMaxChargeTime = 0.35f;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;

    [Header("Coyote Time & Jump Buffer")]
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Dash (8-dir en aire, horizontal en suelo)")]
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashDistance = 6f;
    [SerializeField] private int dashMaxCharges = 2;
    [SerializeField] private float dashCooldown = 1.25f;
    [SerializeField] private float dashBufferTime = 0.12f;

    [Header("Downward Dash / Hold-to-Ground")]
    [SerializeField] private bool allowDownwardDash = true;
    [Tooltip("Activa el dash infinito hacia abajo/diagonal hasta tocar suelo (reemplaza el viejo 'slam').")]
    [SerializeField] private bool slamOnDownwardDash = true; // modo hold-to-ground
    [Tooltip("El dash descendente usa dashSpeed / divisor.")]
    [SerializeField] private float downwardDashSpeedDivisor = 2f;
    [Tooltip("Skin para parar antes de penetrar el suelo.")]
    [SerializeField] private float downwardGroundStopSkin = 0.025f;
    [Tooltip("Fuerza vertical del salto EXTRA al cancelar el downward dash (no es cargable).")]
    [SerializeField] private float downwardCancelJumpForce = 9f;

    [Header("Dash Wall Safety")]
    [SerializeField] private float dashWallSafeDistance = 0.05f;

    [Header("Ground Check (Raycast, dual)")]
    [SerializeField] private Vector2 groundRayOffsetLeft = new Vector2(-0.4f, -0.5f);
    [SerializeField] private Vector2 groundRayOffsetRight = new Vector2(0.4f, -0.5f);
    [SerializeField] private float groundRayLength = 0.2f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private string groundTag = "Ground";

    [Header("Wall Check (dual por lado)")]
    [SerializeField] private Vector2 wallLeftOffsetA = new Vector2(-0.5f, 0.2f);
    [SerializeField] private Vector2 wallLeftOffsetB = new Vector2(-0.5f, -0.2f);
    [SerializeField] private Vector2 wallRightOffsetA = new Vector2(0.5f, 0.2f);
    [SerializeField] private Vector2 wallRightOffsetB = new Vector2(0.5f, -0.2f);
    [SerializeField] private float wallRayLength = 0.12f;
    [SerializeField] private float wallSkinPush = 0.02f;

    [Header("Wall Grab / Slide / Jump")]
    [SerializeField] private bool enableWallGrab = true;
    [SerializeField] private float wallGrabHoldTime = 0.2f;
    [SerializeField] private float wallSlideGravityMultiplier = 0.6f;
    [SerializeField] private float wallRegrabCooldown = 0.15f;
    [SerializeField] private float wallJumpHorizontalForce = 8f;   // (compat)
    [SerializeField] private float wallJumpVerticalForce = 10f;    // (compat)
    [SerializeField] private float wallJumpOppositeMultiplier = 1.3f;
    [SerializeField] private float wallJumpHorizontalLaunchSpeed = 10f;
    [SerializeField] private float wallJumpLockTime = 0.12f;
    [SerializeField] private bool wallCountsAsGroundForDash = true;

    [Header("Sprite Flip")]
    [SerializeField] private Transform spriteChild;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // === Apex Bonus ===
    [Header("Apex Bonus")]
    public float apexVyThreshold = 1.0f;
    public float apexBonusSpeedMultiplier = 1.15f;
    public float apexBonusAccelMultiplier = 1.15f;

    // === Velocidad que crece en suelo ===
    [Header("Ground Run Speed Gain")]
    public float runSpeedMin = 0f;         // si es 0, se toma baseSpeed en Awake
    public float runSpeedMax = 8f;
    public float runGainPerSecond = 2.0f;
    public float runDecayPerSecond = 4.0f;

    // === One-way Platforms (Drop-through) ===
    [Header("One-Way Platforms (Drop-Through)")]
    public bool allowDropThrough = true;
    public LayerMask oneWayMask;           // capa(s) de plataformas one-way
    public string oneWayTag = "OneWay";    // opcional
    public float dropThroughDuration = 0.25f;
    public bool treatOneWayAsGround = true;

    // === Supresión de input hacia la pared tras wall-jump ===
    [Header("Wall Input Suppress")]
    [Tooltip("Tiempo que se ignora el input hacia la pared tras saltar presionando contra ella.")]
    public float wallInputSuppressTime = 0.4f;

    private Rigidbody2D rb;
    private Collider2D col;
    private float currentVelX;
    private int moveDir = 0;
    private int lastPressedDir = 0;
    private bool facingRight;

    // Ground & jump
    private bool isGrounded = false;
    private bool canDoubleJump = false;

    // Jump hold
    private bool isJumpingHoldPhase = false;
    private bool jumpHeld = false;
    private float jumpHoldTimer = 0f;
    private float jumpExtraImpulseLeft = 0f;
    private float jumpExtraImpulsePerSec = 0f;

    // Dash
    private bool isDashing = false;
    private Vector2 dashDir = Vector2.zero;

    // Downward/Diagonal-hold dash state
    private bool isDownwardHoldDash = false;
    private bool cancelDownwardDashQueued = false; // <<< flag de cancelación leído en Update

    // Dash charges
    private float[] dashCooldownLeft;
    private bool[] dashReady;
    private bool[] dashAwaitGround;

    // Fall / slam (legacy)
    private float fallTimer = 0f;
    private bool wasFalling = false;
    private bool isSlamming = false;

    // Coyote & buffers
    private float coyoteTimer = 0f;
    private float jumpBufferTimer = 0f;
    private float dashBufferTimer = 0f;

    // Wall states
    private enum WallSide { None, Left, Right }
    private WallSide wallSide = WallSide.None;
    private bool isOnWall = false;
    private bool isWallGrabbing = false;
    private bool isWallSliding = false;
    private float wallGrabTimer = 0f;
    private float wallRegrabTimer = 0f;

    // Wall-jump lock
    private float wallJumpLockTimer = 0f;

    // Supresión de input hacia pared
    private float wallInputSuppressTimer = 0f;
    private WallSide suppressedWallSide = WallSide.None;

    private enum FallRampContext { None, FromJump, FromDash }
    private FallRampContext fallContext = FallRampContext.None;

    private bool executedJumpThisFrame = false;

    // Velocidad "base" dinámica para correr en suelo
    private float currentRunSpeed;

    // Multiplicadores actuales por apex
    private float apexSpeedMultNow = 1f;
    private float apexAccelMultNow = 1f;

    // OneWay tracking
    private Collider2D lastGroundCollider = null;
    private bool dropThroughActive = false;
    private Collider2D dropThroughCollider = null;
    private float dropThroughTimer = 0f;

    // WallGrab lock mientras dura el drop-through real
    private bool dropBlockWallGrabActive = false;

    // OneWays ignoradas durante dash ascendente
    private readonly List<Collider2D> tempIgnoredOneWays = new List<Collider2D>();

    private void Awake() {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.gravityScale = Mathf.Max(0.1f, rb.gravityScale);
        facingRight = faceRightDefault;

        // Inicializa charges de dash
        int n = Mathf.Max(1, dashMaxCharges);
        dashCooldownLeft = new float[n];
        dashReady = new bool[n];
        dashAwaitGround = new bool[n];
        for (int i = 0; i < n; i++) {
            dashCooldownLeft[i] = 0f;
            dashReady[i] = true;
            dashAwaitGround[i] = false;
        }

        // Inicializa velocidad dinámica de suelo
        if (runSpeedMin <= 0f) runSpeedMin = baseSpeed;
        currentRunSpeed = Mathf.Max(runSpeedMin, baseSpeed);
        runSpeedMax = Mathf.Max(runSpeedMax, runSpeedMin);
    }
    private void Update() {
        executedJumpThisFrame = false;

        // Tick supresión input hacia pared
        if (wallInputSuppressTimer > 0f) {
            wallInputSuppressTimer -= Time.deltaTime;
            if (wallInputSuppressTimer <= 0f) {
                wallInputSuppressTimer = 0f;
                suppressedWallSide = WallSide.None; // habilitar de nuevo el input hacia esa pared
            }
        }

        ReadHorizontalInput();
        HandleFacing();

        // Ground check + coyote + fin de slam al pisar
        isGrounded = CheckGrounded();
        if (isGrounded) {
            canDoubleJump = true;
            coyoteTimer = coyoteTime;
            fallContext = FallRampContext.None;
            isSlamming = false;
        }
        else if (coyoteTimer > 0f) {
            coyoteTimer -= Time.deltaTime;
        }

        // Wall check / estados (bloqueado si drop-through activo)
        UpdateWallDetectionAndStates(Time.deltaTime);

        // Inputs salto
        bool jumpDown = Input.GetKeyDown(jumpKey);
        bool jumpUp = Input.GetKeyUp(jumpKey);
        jumpHeld = Input.GetKey(jumpKey);

        // CANCELACIÓN del downward dash: flag en Update
        if (isDownwardHoldDash && jumpDown) {
            cancelDownwardDashQueued = true;
        }

        // Drop-through: S + salto sobre one-way (sólo si no estamos en dash)
        bool consumedByDrop = false;
        if (!isDashing && allowDropThrough && jumpDown && Input.GetKey(KeyCode.S) && isGrounded && IsOneWayPlatform(lastGroundCollider)) {
            StartCoroutine(DropThroughRoutine(lastGroundCollider, dropThroughDuration));
            executedJumpThisFrame = true;
            consumedByDrop = true;     // no cargar buffer ni procesar salto
        }

        // Buffer de salto (si NO se consumió por drop-through y NO estamos en downward hold dash)
        if (!consumedByDrop && !isDownwardHoldDash) {
            if (jumpDown) jumpBufferTimer = jumpBufferTime;
            else if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;
        }
        else {
            jumpBufferTimer = 0f;
        }

        // Saltos (incluye wall-jump con lock) - sólo si no se consumió por drop-through y no estamos en downward hold dash
        if (!executedJumpThisFrame && !isDownwardHoldDash) {
            HandleJumpInput_ImmediatePressHold(jumpDown, jumpUp);
        }

        // Dash + buffer (prioridad salto). No iniciar nuevo dash si ya hay uno activo.
        if (!isDashing && Input.GetKeyDown(dashKey)) {
            bool fired = TryDashNow();      // valida distancia con cast
            if (!fired) dashBufferTimer = dashBufferTime;
        }
        else if (dashBufferTimer > 0f && !isDashing) {
            dashBufferTimer -= Time.deltaTime;
            if (dashBufferTimer > 0f && !executedJumpThisFrame) {
                TryDashNow();
            }
        }

        // Cargas de dash
        bool groundLike = isGrounded || (isOnWall && wallCountsAsGroundForDash);
        UpdateDashCharges(Time.deltaTime, groundLike);

        // Tick del lock de wall-jump
        if (wallJumpLockTimer > 0f) wallJumpLockTimer -= Time.deltaTime;

        // Tick drop-through (solo para filtro de raycasts de suelo)
        if (dropThroughActive) {
            dropThroughTimer -= Time.deltaTime;
            if (dropThroughTimer <= 0f) {
                dropThroughActive = false;
                dropThroughCollider = null;
            }
        }
    }

    private void FixedUpdate() {
        if (isDashing) return; // el dash mueve por corrutina

        float dt = Time.fixedDeltaTime;

        // === Multiplicadores por Apex ===
        UpdateApexMultipliers();

        // === Velocidad dinámica de suelo ===
        UpdateGroundRunSpeed(dt);

        float effectiveBaseSpeed = Mathf.Clamp(currentRunSpeed, runSpeedMin, runSpeedMax);
        float speedWithApex = effectiveBaseSpeed * speedMultiplier * apexSpeedMultNow;
        float accelWithApex = accel * apexAccelMultNow;

        float targetSpeed = moveDir * speedWithApex;

        // === Horizontal ===
        if (wallJumpLockTimer > 0f) {
            rb.velocity = new Vector2(currentVelX, rb.velocity.y);
        }
        else if (isGrounded || moveDir != 0) {
            currentVelX = Mathf.MoveTowards(currentVelX, targetSpeed, accelWithApex * dt);
            rb.velocity = new Vector2(currentVelX, rb.velocity.y);
        }
        else {
            currentVelX = Mathf.MoveTowards(currentVelX, 0f, airDrag * dt);
            rb.velocity = new Vector2(currentVelX, rb.velocity.y);
        }

        // WallGrab: si estás pegado (y no deslizando), inmoviliza por completo
        if (isWallGrabbing && !isWallSliding && !dropBlockWallGrabActive) {
            rb.velocity = Vector2.zero;
        }

        ApplyJumpHoldBoostInFixed();
        ApplyMarioStyleGravities();
    }

    #region Input & Facing
    private void ReadHorizontalInput() {
        bool leftHeld = Input.GetKey(KeyCode.A);
        bool rightHeld = Input.GetKey(KeyCode.D);

        if (Input.GetKeyDown(KeyCode.A)) lastPressedDir = -1;
        if (Input.GetKeyDown(KeyCode.D)) lastPressedDir = 1;

        if (wallJumpLockTimer > 0f) {
            return;
        }

        // Calcular moveDir tentativo por estado de teclas
        if (leftHeld && rightHeld) {
            moveDir = lastPressedDir;
        }
        else if (leftHeld) {
            moveDir = -1;
        }
        else if (rightHeld) {
            moveDir = 1;
        }
        else {
            moveDir = 0;
        }

        // Aplicar SUPRESIÓN hacia la pared (si activa)
        if (wallInputSuppressTimer > 0f && suppressedWallSide != WallSide.None) {
            if (suppressedWallSide == WallSide.Left && leftHeld) {
                // Si también mantiene derecha, priorizamos derecha; si no, 0
                moveDir = rightHeld ? 1 : (rightHeld ? 1 : 0);
                if (!rightHeld) moveDir = 0;
            }
            else if (suppressedWallSide == WallSide.Right && rightHeld) {
                moveDir = leftHeld ? -1 : (leftHeld ? -1 : 0);
                if (!leftHeld) moveDir = 0;
            }

            // Caso ambas teclas con lastPressedDir apuntando al lado suprimido:
            if (leftHeld && rightHeld) {
                if (suppressedWallSide == WallSide.Left && lastPressedDir < 0) moveDir = 1;
                if (suppressedWallSide == WallSide.Right && lastPressedDir > 0) moveDir = -1;
            }
        }
    }

    private void HandleFacing() {
        if (moveDir != 0 && wallJumpLockTimer <= 0f) {
            bool shouldFaceRight = moveDir > 0;
            if (shouldFaceRight != facingRight) {
                facingRight = shouldFaceRight;
                FlipSprite();
            }
        }
    }

    private void FlipSprite() {
        if (spriteChild != null) {
            Vector3 s = spriteChild.localScale;
            s.x *= -1f;
            spriteChild.localScale = s;
        }
    }
    #endregion
    #region Ground & Wall Checks
    private bool CheckGrounded() {
        bool l = CheckGroundRayAt(groundRayOffsetLeft);
        bool r = CheckGroundRayAt(groundRayOffsetRight);
        return l || r;
    }

    private bool IsSameCollider(Collider2D a, Collider2D b) {
        if (a == null || b == null) return false;
        return a == b;
    }

    private bool CheckGroundRayAt(Vector2 localOffset) {
        Vector2 origin = (Vector2)transform.position + localOffset;

        // 1) Primero intentamos con groundMask
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundRayLength, groundMask);
        if (hit.collider != null) {
            // Ignorar si es la one-way actualmente en drop-through
            if (!(dropThroughActive && IsSameCollider(hit.collider, dropThroughCollider))) {
                lastGroundCollider = hit.collider;
                return true;
            }
        }

        // 2) Sin máscara: aceptar por tag de suelo, o por one-way si está permitido
        RaycastHit2D hitNoMask = Physics2D.Raycast(origin, Vector2.down, groundRayLength);
        if (hitNoMask.collider != null) {
            Collider2D c = hitNoMask.collider;

            if (dropThroughActive && IsSameCollider(c, dropThroughCollider)) {
                return false;
            }

            if (c.CompareTag(groundTag)) {
                lastGroundCollider = c;
                return true;
            }

            if (treatOneWayAsGround && IsOneWayPlatform(c)) {
                lastGroundCollider = c;
                return true;
            }
        }

        return false;
    }

    private void UpdateWallDetectionAndStates(float dt) {
        // Bloqueo TOTAL del sistema de pared mientras dura el drop-through real
        if (!enableWallGrab || dropBlockWallGrabActive) {
            isOnWall = isWallGrabbing = isWallSliding = false;
            wallSide = WallSide.None;
            if (wallRegrabTimer > 0f) wallRegrabTimer -= dt;
            return;
        }

        bool left = CastWallAt(wallLeftOffsetA, Vector2.left) || CastWallAt(wallLeftOffsetB, Vector2.left);
        bool right = CastWallAt(wallRightOffsetA, Vector2.right) || CastWallAt(wallRightOffsetB, Vector2.right);

        WallSide newSide = WallSide.None;
        if (left && !right) newSide = WallSide.Left;
        else if (right && !left) newSide = WallSide.Right;
        else if (left && right) newSide = (lastPressedDir >= 0) ? WallSide.Right : WallSide.Left;

        bool wasOnWall = isOnWall;
        WallSide prevSide = wallSide;

        isOnWall = (newSide != WallSide.None) && !isGrounded;
        wallSide = isOnWall ? newSide : WallSide.None;

        if (wallRegrabTimer > 0f) wallRegrabTimer -= dt;

        if (isOnWall) {
            if (!wasOnWall || (prevSide != wallSide)) {
                if (wallRegrabTimer <= 0f) {
                    EnterWallGrab();
                }
            }

            if (isWallGrabbing && !isWallSliding) {
                wallGrabTimer += dt;
                if (wallGrabTimer >= wallGrabHoldTime) {
                    EnterWallSlide();
                }
            }
        }
        else {
            if (wasOnWall) {
                ExitWallStates();
                wallRegrabTimer = wallRegrabCooldown;
            }
        }
    }

    private bool CastWallAt(Vector2 localOffset, Vector2 dir) {
        Vector2 origin = (Vector2)transform.position + localOffset;
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, wallRayLength, groundMask);
        if (hit.collider != null) return true;

        RaycastHit2D hitNoMask = Physics2D.Raycast(origin, dir, wallRayLength);
        return (hitNoMask.collider != null && hitNoMask.collider.CompareTag(groundTag));
    }

    private void EnterWallGrab() {
        isWallGrabbing = true;
        isWallSliding = false;
        wallGrabTimer = 0f;
        isSlamming = false;

        float push = wallSkinPush;
        if (wallSide == WallSide.Left) transform.position += new Vector3(+push, 0f, 0f);
        if (wallSide == WallSide.Right) transform.position += new Vector3(-push, 0f, 0f);

        canDoubleJump = true;
    }

    private void EnterWallSlide() {
        isWallGrabbing = false;
        isWallSliding = true;
    }

    private void ExitWallStates() {
        isWallGrabbing = false;
        isWallSliding = false;
        wallGrabTimer = 0f;
        wallSide = WallSide.None;
    }
    #endregion

    #region Jump (Immediate + Hold + Coyote + Buffer + Wall Jump con lock)
    private void HandleJumpInput_ImmediatePressHold(bool jumpDown, bool jumpUp) {
        // Wall-jump tiene prioridad si estamos en pared
        if ((isWallGrabbing || isWallSliding) && (jumpDown || (jumpBufferTimer > 0f))) {
            DoWallJump();
            jumpBufferTimer = 0f;
            executedJumpThisFrame = true;
            return;
        }

        bool canGroundLikeJumpNow = isGrounded || (coyoteTimer > 0f);

        if (jumpBufferTimer > 0f && canGroundLikeJumpNow) {
            DoGroundLikeJump();
            jumpBufferTimer = 0f;
            executedJumpThisFrame = true;
            return;
        }

        if (jumpDown) {
            if (canGroundLikeJumpNow) {
                jumpBufferTimer = 0f;
                DoGroundLikeJump();
                executedJumpThisFrame = true;
                return;
            }
            if (!isGrounded && canDoubleJump) {
                DoDoubleJump();
                executedJumpThisFrame = true;
                return;
            }
        }

        if (jumpUp) {
            isJumpingHoldPhase = false;
        }
    }

    private void DoGroundLikeJump() {
        rb.velocity = new Vector2(rb.velocity.x, 0f);
        wasFalling = false;
        fallTimer = 0f;
        isSlamming = false;
        ExitWallStates();
        wallJumpLockTimer = 0f;
        fallContext = FallRampContext.FromJump;

        float riseScale = Mathf.Sqrt(Mathf.Max(0.01f, riseGravityMultiplier));

        float initialImpulse = jumpMinForce * riseScale;
        rb.AddForce(Vector2.up * initialImpulse, ForceMode2D.Impulse);

        float extraTotal = Mathf.Max(0f, (jumpMaxForce - jumpMinForce) * riseScale);
        jumpExtraImpulseLeft = extraTotal;
        jumpExtraImpulsePerSec = (jumpMaxChargeTime > 0f) ? (extraTotal / jumpMaxChargeTime) : (extraTotal / Mathf.Epsilon);

        isJumpingHoldPhase = true;
        jumpHoldTimer = 0f;
    }

    private void DoDoubleJump() {
        rb.velocity = new Vector2(rb.velocity.x, 0f);
        canDoubleJump = false;
        wasFalling = false;
        fallTimer = 0f;
        isSlamming = false;
        ExitWallStates();
        wallJumpLockTimer = 0f;
        fallContext = FallRampContext.FromJump;

        float riseScale = Mathf.Sqrt(Mathf.Max(0.01f, riseGravityMultiplier));

        float initialImpulse = jumpMinForce * riseScale;
        rb.AddForce(Vector2.up * initialImpulse, ForceMode2D.Impulse);

        float extraTotal = Mathf.Max(0f, (jumpMaxForce - jumpMinForce) * riseScale);
        jumpExtraImpulseLeft = extraTotal;
        jumpExtraImpulsePerSec = (jumpMaxChargeTime > 0f) ? (extraTotal / jumpMaxChargeTime) : (extraTotal / Mathf.Epsilon);
        isJumpingHoldPhase = true;
        jumpHoldTimer = 0f;
    }

    // Salto EXTRA para cancelar downward dash (no consume doble salto, no es cargable)
    private void DoDownwardCancelJump() {
        rb.velocity = new Vector2(rb.velocity.x, 0f);
        wasFalling = false;
        fallTimer = 0f;
        isSlamming = false;
        ExitWallStates();
        wallJumpLockTimer = 0f;
        fallContext = FallRampContext.FromJump;

        // Impulso fijo (no-hold)
        float impulse = Mathf.Max(0f, downwardCancelJumpForce);
        rb.AddForce(Vector2.up * impulse, ForceMode2D.Impulse);

        // Desactiva cualquier fase de hold
        isJumpingHoldPhase = false;
        jumpExtraImpulseLeft = 0f;
        jumpHoldTimer = 0f;
    }
    private void DoWallJump() {
        // Guardar el lado de pared actual antes de resetear estados
        WallSide prevWallSide = wallSide;

        int away = (prevWallSide == WallSide.Right) ? -1 : +1;

        // ¿El jugador está manteniendo input hacia la pared en el momento del salto?
        bool towardWall = (prevWallSide == WallSide.Left && Input.GetKey(KeyCode.A))
                       || (prevWallSide == WallSide.Right && Input.GetKey(KeyCode.D));

        // Bonus si mantiene input opuesto (hacia afuera)
        bool inputOpposite = (away < 0 && Input.GetKey(KeyCode.A)) || (away > 0 && Input.GetKey(KeyCode.D));
        float launchX = wallJumpHorizontalLaunchSpeed * (inputOpposite ? wallJumpOppositeMultiplier : 1f);

        // Lanzamiento estándar alejándose de la pared
        rb.velocity = Vector2.zero;
        ExitWallStates();
        wasFalling = false;
        fallTimer = 0f;
        isSlamming = false;
        fallContext = FallRampContext.FromJump;

        currentVelX = away * launchX;
        rb.velocity = new Vector2(currentVelX, 0f);
        rb.AddForce(Vector2.up * wallJumpVerticalForce, ForceMode2D.Impulse);

        // Arranca fase de hold (como un salto normal)
        float riseScale = Mathf.Sqrt(Mathf.Max(0.01f, riseGravityMultiplier));
        float extraTotal = Mathf.Max(0f, (jumpMaxForce - jumpMinForce) * riseScale);
        jumpExtraImpulseLeft = extraTotal;
        jumpExtraImpulsePerSec = (jumpMaxChargeTime > 0f) ? (extraTotal / jumpMaxChargeTime) : (extraTotal / Mathf.Epsilon);
        isJumpingHoldPhase = true;
        jumpHoldTimer = 0f;

        // Lock de control horizontal normal del wall-jump
        wallJumpLockTimer = Mathf.Max(0f, wallJumpLockTime);
        wallRegrabTimer = wallRegrabCooldown;

        // SUPRESIÓN: si estaba manteniendo hacia la pared, ignorar ese input por un tiempo
        if (towardWall) {
            suppressedWallSide = prevWallSide;
            wallInputSuppressTimer = Mathf.Max(0f, wallInputSuppressTime);
        }

        // Orientar sprite hacia movimiento si hace falta
        if ((away > 0 && !facingRight) || (away < 0 && facingRight)) {
            facingRight = !facingRight;
            FlipSprite();
        }
    }

    private void ApplyJumpHoldBoostInFixed() {
        if (!isJumpingHoldPhase) return;
        if (!jumpHeld || jumpHoldTimer >= jumpMaxChargeTime || jumpExtraImpulseLeft <= 0f || rb.velocity.y <= 0f) {
            isJumpingHoldPhase = false;
            return;
        }

        float dt = Time.fixedDeltaTime;
        float add = jumpExtraImpulsePerSec * dt;
        if (add > jumpExtraImpulseLeft) add = jumpExtraImpulseLeft;

        rb.AddForce(Vector2.up * add, ForceMode2D.Impulse);

        jumpExtraImpulseLeft -= add;
        jumpHoldTimer += dt;

        if (jumpExtraImpulseLeft <= 0f || jumpHoldTimer >= jumpMaxChargeTime) {
            isJumpingHoldPhase = false;
        }
    }

    private void ApplyMarioStyleGravities() {
        if (isDownwardHoldDash) {
            // Mientras esté en dash descendente infinito, ignoramos gravidades personalizadas.
            return;
        }

        if (isSlamming) {
            float multiplier = fallGravityMultiplier * Mathf.Max(0f, 6f); // legacy
            rb.velocity += Vector2.up * Physics2D.gravity.y * (multiplier) * Time.fixedDeltaTime;
            return;
        }

        if (isWallSliding) {
            float mult = Mathf.Max(0f, wallSlideGravityMultiplier);
            rb.velocity += Vector2.up * Physics2D.gravity.y * mult * Time.fixedDeltaTime;
            return;
        }

        if (rb.velocity.y < 0f) {
            if (!wasFalling) {
                wasFalling = true;
                fallTimer = 0f;
                rb.velocity = new Vector2(rb.velocity.x, 0f);
            }
            else {
                fallTimer += Time.fixedDeltaTime;
            }

            float activeRamp = (fallContext == FallRampContext.FromDash) ? fallRampTimeDash : fallRampTimeJump;
            float t = (activeRamp > 0f) ? Mathf.Clamp01(fallTimer / activeRamp) : 1f;
            float currentMultiplier = Mathf.Lerp(1f, fallGravityMultiplier, t);

            rb.velocity += Vector2.up * Physics2D.gravity.y * (currentMultiplier) * Time.fixedDeltaTime;
        }
        else {
            wasFalling = false;

            if (rb.velocity.y > 0f) {
                rb.velocity += Vector2.up * Physics2D.gravity.y * (riseGravityMultiplier) * Time.fixedDeltaTime;
            }
        }
    }
    #endregion

    #region Dash (cast + skin), charges, gizmos

    private float ComputeDashAllowedDistance(Vector2 dir) {
        if (dir.sqrMagnitude <= 0f) return 0f;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(groundMask);
        filter.useLayerMask = true;

        float maxCheck = Mathf.Max(0f, dashDistance + dashWallSafeDistance);
        RaycastHit2D[] hits = new RaycastHit2D[16];
        int count = col.Cast(dir, filter, hits, maxCheck);

        float minHitDist = maxCheck;
        for (int i = 0; i < count; i++) {
            var h = hits[i];
            if (h.collider == null) continue;

            // si vamos hacia ARRIBA, ignorar plataformas OneWay como obstáculos
            if (dir.y > 0f && IsOneWayPlatform(h.collider)) continue;

            if (h.distance < minHitDist) {
                minHitDist = h.distance;
            }
        }

        float allowed = (minHitDist < maxCheck)
            ? Mathf.Clamp(minHitDist - dashWallSafeDistance, 0f, dashDistance)
            : dashDistance;

        return allowed;
    }

    private bool TryDashNow() {
        if (isDashing) return false;

        int readyIdx = GetReadyDashIndex();
        if (readyIdx < 0) return false;

        Vector2 dir;

        if (isGrounded) {
            bool left = Input.GetKey(KeyCode.A);
            bool right = Input.GetKey(KeyCode.D);
            bool up = Input.GetKey(KeyCode.W);
            bool down = Input.GetKey(KeyCode.S);

            if (up || down) return false;
            if (left == right) return false;

            int hx = left ? -1 : 1;
            dir = new Vector2(hx, 0f);
        }
        else {
            int x = 0, y = 0;
            if (Input.GetKey(KeyCode.D)) x = 1;
            if (Input.GetKey(KeyCode.A)) x = -1;
            if (Input.GetKey(KeyCode.W)) y = 1;
            if (Input.GetKey(KeyCode.S)) y = -1;

            dir = new Vector2(x, y);
            if (dir == Vector2.zero) {
                dir = facingRight ? Vector2.right : Vector2.left;
            }
            else {
                dir = dir.normalized;
            }

            if (!allowDownwardDash && dir.y < 0f) return false;
        }

        // Dash descendente/diagonal hacia abajo con hold-to-ground
        if (slamOnDownwardDash && dir.y < 0f) {
            ConsumeDashCharge(readyIdx);
            StartCoroutine(DownwardHoldDashRoutine(dir));
            return true;
        }

        // Dash normal (hacia lados/arriba)
        float allowedDistance = ComputeDashAllowedDistance(dir);
        if (allowedDistance <= 0.0001f) return false;

        // si dash es ascendente, ignorar temporalmente las OneWay que haya en la trayectoria
        if (dir.y > 0f) {
            PrepareUpwardDashIgnoreOneWays(dir, allowedDistance);
        }

        ConsumeDashCharge(readyIdx);
        StartCoroutine(DashRoutine(dir, allowedDistance));
        return true;
    }

    private IEnumerator DownwardHoldDashRoutine(Vector2 dir) {
        isDashing = true;
        isDownwardHoldDash = true;
        cancelDownwardDashQueued = false;
        dashDir = dir.normalized;
        ExitWallStates();

        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;

        float speed = dashSpeed / Mathf.Max(0.01f, downwardDashSpeedDivisor);

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.useLayerMask = true;
        filter.SetLayerMask(groundMask);

        WaitForFixedUpdate wait = new WaitForFixedUpdate();

        // Loop hasta tocar suelo o cancelación por salto
        while (true) {
            // CANCELACIÓN: flag seteado en Update
            if (cancelDownwardDashQueued) {
                cancelDownwardDashQueued = false;
                isDownwardHoldDash = false;
                isDashing = false;
                rb.gravityScale = originalGravity;

                if (!isGrounded && canDoubleJump) {
                    DoDoubleJump(); // consume el doble salto real
                }
                else {
                    DoDownwardCancelJump(); // salto extra (no consume doble)
                }
                fallContext = FallRampContext.FromDash;
                yield break;
            }

            float dt = Time.fixedDeltaTime;
            float step = speed * dt;

            // Cast hacia la dirección del dash para detectar suelo/obstáculo
            RaycastHit2D[] hits = new RaycastHit2D[8];
            int count = col.Cast(dashDir, filter, hits, step + dashWallSafeDistance);
            bool willHit = false;
            float hitDist = Mathf.Infinity;

            for (int i = 0; i < count; i++) {
                var h = hits[i];
                if (h.collider == null) continue;
                hitDist = Mathf.Min(hitDist, h.distance);
                willHit = true;
            }

            if (willHit) {
                // Moverse justo antes del impacto (skin)
                float moveDist = Mathf.Max(0f, hitDist - downwardGroundStopSkin);
                Vector2 nextPos = rb.position + dashDir * moveDist;
                rb.MovePosition(nextPos);

                // Termina el dash (tocó suelo)
                rb.gravityScale = originalGravity;
                isDownwardHoldDash = false;
                isDashing = false;

                // Al tocar suelo, reseteos de caida
                wasFalling = false;
                fallTimer = 0f;
                fallContext = FallRampContext.FromDash;
                yield break;
            }
            else {
                // Avanzar normalmente
                Vector2 nextPos = rb.position + dashDir * step;
                rb.MovePosition(nextPos);
            }

            yield return wait;
        }
    }

    private IEnumerator DashRoutine(Vector2 dir, float allowedDistance) {
        isDashing = true;
        dashDir = dir;
        isSlamming = false;
        ExitWallStates();

        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;
        Vector2 originalVel = rb.velocity;
        rb.velocity = Vector2.zero;

        Vector2 targetPos = rb.position + dashDir * allowedDistance;

        WaitForFixedUpdate wait = new WaitForFixedUpdate();
        while ((rb.position - targetPos).sqrMagnitude > 0.0001f) {
            float dt = Time.fixedDeltaTime;

            float effectiveBaseSpeed = Mathf.Clamp(currentRunSpeed, runSpeedMin, runSpeedMax);
            float speedWithApex = effectiveBaseSpeed * speedMultiplier * apexSpeedMultNow;
            float targetSpeed = moveDir * speedWithApex;

            if (moveDir != 0) {
                currentVelX = Mathf.MoveTowards(currentVelX, targetSpeed, accel * dt);
            }
            else {
                currentVelX = Mathf.MoveTowards(currentVelX, 0f, airDrag * dt);
            }

            float step = dashSpeed * Time.fixedDeltaTime;
            Vector2 nextPos = Vector2.MoveTowards(rb.position, targetPos, step);
            rb.MovePosition(nextPos);

            yield return wait;
        }

        rb.gravityScale = originalGravity;

        // Fin dash normal
        rb.velocity = new Vector2(currentVelX, 0f);
        wasFalling = false;
        fallTimer = 0f;
        fallContext = FallRampContext.FromDash;

        // restaurar colisiones con OneWay ignoradas durante dash ascendente
        RestoreIgnoredOneWaysAfterDash();

        isDashing = false;
    }

    private void UpdateDashCharges(float dt, bool groundLikeNow) {
        int n = dashCooldownLeft.Length;
        for (int i = 0; i < n; i++) {
            if (dashReady[i]) continue;

            if (dashCooldownLeft[i] > 0f) {
                dashCooldownLeft[i] -= dt;
                if (dashCooldownLeft[i] <= 0f) {
                    dashCooldownLeft[i] = 0f;
                    if (groundLikeNow) {
                        dashReady[i] = true;
                        dashAwaitGround[i] = false;
                    }
                    else {
                        dashAwaitGround[i] = true;
                    }
                }
            }
            else {
                if (dashAwaitGround[i] && groundLikeNow) {
                    dashReady[i] = true;
                    dashAwaitGround[i] = false;
                }
            }
        }
    }

    private int GetReadyDashIndex() {
        for (int i = 0; i < dashReady.Length; i++) {
            if (dashReady[i]) return i;
        }
        return -1;
    }

    private void ConsumeDashCharge(int idx) {
        dashReady[idx] = false;
        dashAwaitGround[idx] = false;
        dashCooldownLeft[idx] = dashCooldown;
    }
    #endregion

    // === Apex helpers ===
    private void UpdateApexMultipliers() {
        float vy = rb.velocity.y;
        float thr = Mathf.Max(0.0001f, apexVyThreshold);

        float nearApex01 = 1f - Mathf.Clamp01(Mathf.Abs(vy) / thr);

        apexSpeedMultNow = Mathf.Lerp(1f, Mathf.Max(1f, apexBonusSpeedMultiplier), nearApex01);
        apexAccelMultNow = Mathf.Lerp(1f, Mathf.Max(1f, apexBonusAccelMultiplier), nearApex01);
    }

    // === Ground run speed gain/decay ===
    private void UpdateGroundRunSpeed(float dt) {
        bool hasInput = (moveDir != 0);

        if (isGrounded && hasInput) {
            currentRunSpeed = Mathf.MoveTowards(currentRunSpeed, runSpeedMax, runGainPerSecond * dt);
        }
        else {
            currentRunSpeed = Mathf.MoveTowards(currentRunSpeed, runSpeedMin, runDecayPerSecond * dt);
        }
    }

    // === One-way helpers ===
    private bool IsOneWayPlatform(Collider2D c) {
        if (c == null) return false;

        // por capa
        if ((oneWayMask.value & (1 << c.gameObject.layer)) != 0) return true;

        // por tag
        if (!string.IsNullOrEmpty(oneWayTag) && c.CompareTag(oneWayTag)) return true;

        // por componente PlatformEffector2D
        if (c.GetComponent<PlatformEffector2D>() != null) return true;
        if (c.GetComponentInParent<PlatformEffector2D>() != null) return true;

        return false;
    }

    // Ignora temporalmente las OneWay en la trayectoria del dash ascendente
    private void PrepareUpwardDashIgnoreOneWays(Vector2 dir, float distance) {
        tempIgnoredOneWays.Clear();
        if (oneWayMask.value == 0) return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.useLayerMask = true;
        filter.SetLayerMask(oneWayMask);

        RaycastHit2D[] hits = new RaycastHit2D[16];
        int count = col.Cast(dir, filter, hits, distance);
        for (int i = 0; i < count; i++) {
            var c = hits[i].collider;
            if (c == null) continue;
            if (!IsOneWayPlatform(c)) continue;

            if (!tempIgnoredOneWays.Contains(c)) {
                Physics2D.IgnoreCollision(col, c, true);
                tempIgnoredOneWays.Add(c);
            }
        }
    }

    private void RestoreIgnoredOneWaysAfterDash() {
        for (int i = 0; i < tempIgnoredOneWays.Count; i++) {
            var c = tempIgnoredOneWays[i];
            if (c != null) Physics2D.IgnoreCollision(col, c, false);
        }
        tempIgnoredOneWays.Clear();
    }

    private IEnumerator DropThroughRoutine(Collider2D platform, float duration) {
        if (platform == null) yield break;

        // Activamos modo drop-through
        dropThroughActive = true;
        dropThroughCollider = platform;
        dropThroughTimer = duration;

        // Bloquea el sistema de wall-grab SOLO durante el tiempo real de drop
        dropBlockWallGrabActive = true;

        // Ignoramos colisión con esa plataforma por un rato
        Physics2D.IgnoreCollision(col, platform, true);

        // Forzamos salir del estado de suelo y empujón leve hacia abajo
        isGrounded = false;
        rb.velocity = new Vector2(rb.velocity.x, Mathf.Min(rb.velocity.y, -0.1f));

        // Espera duración
        float t = 0f;
        while (t < duration) {
            t += Time.deltaTime;
            yield return null;
        }

        // Rehabilitamos la colisión
        Physics2D.IgnoreCollision(col, platform, false);

        // Fin del bloqueo de wall-grab (ya salimos del volumen de la plataforma)
        dropBlockWallGrabActive = false;

        // Pequeña tolerancia para que el raycast no la detecte inmediatamente
        dropThroughTimer = 0.05f;
        dropThroughActive = true;
        dropThroughCollider = platform;
    }

    private void OnDrawGizmosSelected() {
        if (!drawGizmos) return;
        Gizmos.color = Color.green;

        // Suelo
        Vector2 originL = (Vector2)transform.position + groundRayOffsetLeft;
        Vector2 originR = (Vector2)transform.position + groundRayOffsetRight;
        Gizmos.DrawLine(originL, originL + Vector2.down * groundRayLength);
        Gizmos.DrawLine(originR, originR + Vector2.down * groundRayLength);

        // Pared izquierda
        Vector2 wlA = (Vector2)transform.position + wallLeftOffsetA;
        Vector2 wlB = (Vector2)transform.position + wallLeftOffsetB;
        Gizmos.DrawLine(wlA, wlA + Vector2.left * wallRayLength);
        Gizmos.DrawLine(wlB, wlB + Vector2.left * wallRayLength);

        // Pared derecha
        Vector2 wrA = (Vector2)transform.position + wallRightOffsetA;
        Vector2 wrB = (Vector2)transform.position + wallRightOffsetB;
        Gizmos.DrawLine(wrA, wrA + Vector2.right * wallRayLength);
        Gizmos.DrawLine(wrB, wrB + Vector2.right * wallRayLength);
    }

    // API pública
    public void SetSpeedMultiplier(float multiplier) {
        speedMultiplier = Mathf.Max(0f, multiplier);
    }

    public float CurrentHorizontalSpeed => currentVelX;
    public bool IsGrounded => isGrounded;

    public int CurrentDashCharges {
        get {
            int count = 0;
            for (int i = 0; i < dashReady.Length; i++) if (dashReady[i]) count++;
            return count;
        }
    }
}
