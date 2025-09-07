using UnityEngine;
using System.Collections;

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

    [Header("Downward Dash / Slam")]
    [SerializeField] private bool allowDownwardDash = true;
    [SerializeField] private bool slamOnDownwardDash = true;
    [SerializeField] private float slamFallMultiplier = 6f;

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
    [SerializeField] private float wallJumpHorizontalForce = 8f;   // (se mantiene para compatibilidad)
    [SerializeField] private float wallJumpVerticalForce = 10f;  // (se mantiene para compatibilidad)
    [SerializeField] private float wallJumpOppositeMultiplier = 1.3f;
    [SerializeField] private float wallJumpHorizontalLaunchSpeed = 10f; // <<< NUEVO: velocidad X de lanzamiento
    [SerializeField] private float wallJumpLockTime = 0.12f;            // <<< NUEVO: tiempo de bloqueo de control horario
    [SerializeField] private bool wallCountsAsGroundForDash = true;

    [Header("Sprite Flip")]
    [SerializeField] private Transform spriteChild;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

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

    // Dash charges
    private float[] dashCooldownLeft;
    private bool[] dashReady;
    private bool[] dashAwaitGround;

    // Fall / slam
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
    private float wallJumpLockTimer = 0f; // <<< NUEVO

    private enum FallRampContext { None, FromJump, FromDash }
    private FallRampContext fallContext = FallRampContext.None;

    private bool executedJumpThisFrame = false;

    private void Awake() {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.gravityScale = Mathf.Max(0.1f, rb.gravityScale);
        facingRight = faceRightDefault;

        int n = Mathf.Max(1, dashMaxCharges);
        dashCooldownLeft = new float[n];
        dashReady = new bool[n];
        dashAwaitGround = new bool[n];
        for (int i = 0; i < n; i++) {
            dashCooldownLeft[i] = 0f;
            dashReady[i] = true;
            dashAwaitGround[i] = false;
        }
    }

    private void Update() {
        executedJumpThisFrame = false;

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

        // Wall check / estados
        UpdateWallDetectionAndStates(Time.deltaTime);

        // Inputs salto
        bool jumpDown = Input.GetKeyDown(jumpKey);
        bool jumpUp = Input.GetKeyUp(jumpKey);
        jumpHeld = Input.GetKey(jumpKey);

        // Buffer de salto
        if (jumpDown) jumpBufferTimer = jumpBufferTime;
        else if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;

        // Saltos (incluye wall-jump con lock)
        HandleJumpInput_ImmediatePressHold(jumpDown, jumpUp);

        // Dash + buffer (prioridad salto)
        if (Input.GetKeyDown(dashKey)) {
            bool fired = TryDashNow();      // valida distancia con cast
            if (!fired) dashBufferTimer = dashBufferTime;
        }
        else if (dashBufferTimer > 0f) {
            dashBufferTimer -= Time.deltaTime;
            if (dashBufferTimer > 0f && !executedJumpThisFrame) {
                TryDashNow();
            }
        }

        // Cargas de dash: "ground-like" si en pared y permitido
        bool groundLike = isGrounded || (isOnWall && wallCountsAsGroundForDash);
        UpdateDashCharges(Time.deltaTime, groundLike);

        // Tick del lock de wall-jump
        if (wallJumpLockTimer > 0f) wallJumpLockTimer -= Time.deltaTime;
    }
    private void FixedUpdate() {
        if (isDashing) return; // el dash mueve por corrutina

        float dt = Time.fixedDeltaTime;
        float targetSpeed = moveDir * baseSpeed * speedMultiplier;

        // === Horizontal ===
        if (wallJumpLockTimer > 0f) {
            // Durante el lock, conservamos la velocidad horizontal de lanzamiento
            rb.velocity = new Vector2(currentVelX, rb.velocity.y);
        }
        else if (isGrounded || moveDir != 0) {
            currentVelX = Mathf.MoveTowards(currentVelX, targetSpeed, accel * dt);
            rb.velocity = new Vector2(currentVelX, rb.velocity.y);
        }
        else {
            currentVelX = Mathf.MoveTowards(currentVelX, 0f, airDrag * dt);
            rb.velocity = new Vector2(currentVelX, rb.velocity.y);
        }

        // WallGrab: si estás pegado (y no deslizando), inmoviliza por completo
        if (isWallGrabbing && !isWallSliding) {
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
            // Ignorar cambios de input durante el lock (no tocamos moveDir)
            return;
        }

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
        return CheckGroundRayAt(groundRayOffsetLeft) || CheckGroundRayAt(groundRayOffsetRight);
    }

    private bool CheckGroundRayAt(Vector2 localOffset) {
        Vector2 origin = (Vector2)transform.position + localOffset;

        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundRayLength, groundMask);
        if (hit.collider != null) return true;

        RaycastHit2D hitNoMask = Physics2D.Raycast(origin, Vector2.down, groundRayLength);
        if (hitNoMask.collider != null && hitNoMask.collider.CompareTag(groundTag)) return true;

        return false;
    }

    private void UpdateWallDetectionAndStates(float dt) {
        if (!enableWallGrab) {
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

    private void DoWallJump() {
        // Siempre alejándose de la pared
        int away = (wallSide == WallSide.Right) ? -1 : +1;

        // Bonus si el jugador mantiene input opuesto
        bool inputOpposite = (away < 0 && Input.GetKey(KeyCode.A)) || (away > 0 && Input.GetKey(KeyCode.D));
        float launchX = wallJumpHorizontalLaunchSpeed * (inputOpposite ? wallJumpOppositeMultiplier : 1f);

        // Reset estados
        rb.velocity = Vector2.zero;
        ExitWallStates();
        wasFalling = false;
        fallTimer = 0f;
        isSlamming = false;
        fallContext = FallRampContext.FromJump;

        // Lanzamiento: fijamos X directa + impulso vertical
        currentVelX = away * launchX;                   // <<< clave: sincroniza con el sistema de movimiento
        rb.velocity = new Vector2(currentVelX, 0f);
        rb.AddForce(Vector2.up * wallJumpVerticalForce, ForceMode2D.Impulse);

        // Arranca fase de hold (como un salto normal)
        float riseScale = Mathf.Sqrt(Mathf.Max(0.01f, riseGravityMultiplier));
        float extraTotal = Mathf.Max(0f, (jumpMaxForce - jumpMinForce) * riseScale);
        jumpExtraImpulseLeft = extraTotal;
        jumpExtraImpulsePerSec = (jumpMaxChargeTime > 0f) ? (extraTotal / jumpMaxChargeTime) : (extraTotal / Mathf.Epsilon);
        isJumpingHoldPhase = true;
        jumpHoldTimer = 0f;

        // Lock de control horizontal para que no lo cancelen ni el input ni el drag
        wallJumpLockTimer = Mathf.Max(0f, wallJumpLockTime);

        // Anti re-enganche inmediato
        wallRegrabTimer = wallRegrabCooldown;

        // Opcional: orientar sprite hacia el movimiento si querés
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
        if (isSlamming) {
            float multiplier = fallGravityMultiplier * Mathf.Max(0f, slamFallMultiplier);
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
        RaycastHit2D[] hits = new RaycastHit2D[8];
        int count = col.Cast(dir, filter, hits, maxCheck);

        float minHitDist = maxCheck;
        for (int i = 0; i < count; i++) {
            if (hits[i].collider == null) continue;
            if (hits[i].distance < minHitDist) {
                minHitDist = hits[i].distance;
            }
        }

        float allowed = (count > 0)
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

        float allowedDistance = ComputeDashAllowedDistance(dir);
        if (allowedDistance <= 0.0001f) {
            return false; // pegado a pared o sin espacio: NO consume carga
        }

        ConsumeDashCharge(readyIdx);
        StartCoroutine(DashRoutine(dir, allowedDistance));
        return true;
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
            float targetSpeed = moveDir * baseSpeed * speedMultiplier;

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

        if (dashDir.y < 0f && slamOnDownwardDash) {
            float startDownVel = -dashSpeed;
            rb.velocity = new Vector2(currentVelX, startDownVel);

            fallContext = FallRampContext.FromDash;
            wasFalling = true;
            fallTimer = 0f;
            isSlamming = true;
        }
        else {
            rb.velocity = new Vector2(currentVelX, 0f);
            wasFalling = false;
            fallTimer = 0f;
            fallContext = FallRampContext.FromDash;
        }

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
