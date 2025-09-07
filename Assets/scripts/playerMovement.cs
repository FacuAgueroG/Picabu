using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
public class Player2DController : MonoBehaviour {
    [Header("Movement")]
    [SerializeField] private float baseSpeed = 6f;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float accel = 999f;                  // aceleración/freno “duro” (suelo / con input)
    [SerializeField] private bool faceRightDefault = true;
    [SerializeField] private float airDrag = 5f;                  // <<< resistencia horizontal en el aire (sin input)

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

    [Header("Ground Check (Raycast, dual)")]
    [SerializeField] private Vector2 groundRayOffsetLeft = new Vector2(-0.4f, -0.5f);
    [SerializeField] private Vector2 groundRayOffsetRight = new Vector2(0.4f, -0.5f);
    [SerializeField] private float groundRayLength = 0.2f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private string groundTag = "Ground";

    [Header("Dash (restricciones + 8 direcciones en aire)")]
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashDistance = 6f;
    [SerializeField] private int dashMaxCharges = 2;
    [SerializeField] private float dashCooldown = 1.25f;

    [Header("Sprite Flip")]
    [SerializeField] private Transform spriteChild;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private Rigidbody2D rb;
    private float currentVelX;
    private int moveDir = 0;
    private int lastPressedDir = 0;
    private bool facingRight;

    // Ground & jump state
    private bool isGrounded = false;
    private bool canDoubleJump = false;

    // Jump hold system
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

    // Fall ramp
    private float fallTimer = 0f;
    private bool wasFalling = false;

    // Coyote & buffer
    private float coyoteTimer = 0f;
    private float jumpBufferTimer = 0f;

    private enum FallRampContext { None, FromJump, FromDash }
    private FallRampContext fallContext = FallRampContext.None;

    private void Awake() {
        rb = GetComponent<Rigidbody2D>();
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
        ReadHorizontalInput();
        HandleFacing();

        // Ground check + coyote
        isGrounded = CheckGrounded();
        if (isGrounded) {
            canDoubleJump = true;
            coyoteTimer = coyoteTime;
            fallContext = FallRampContext.None;
        }
        else if (coyoteTimer > 0f) {
            coyoteTimer -= Time.deltaTime;
        }

        // Inputs salto
        bool jumpDown = Input.GetKeyDown(jumpKey);
        bool jumpUp = Input.GetKeyUp(jumpKey);
        jumpHeld = Input.GetKey(jumpKey);

        // Buffer
        if (jumpDown) jumpBufferTimer = jumpBufferTime;
        else if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;

        HandleJumpInput_ImmediatePressHold(jumpDown, jumpUp);
        HandleDashInput();

        UpdateDashCharges(Time.deltaTime, isGrounded);
    }

    private void FixedUpdate() {
        if (isDashing) return;

        float dt = Time.fixedDeltaTime;
        float targetSpeed = moveDir * baseSpeed * speedMultiplier;

        // --- Movimiento X corregido para drag ---
        if (isGrounded || moveDir != 0) {
            // En suelo (o en aire pero con input): usar aceleración “dura”
            currentVelX = Mathf.MoveTowards(currentVelX, targetSpeed, accel * dt);
        }
        else {
            // En aire y SIN input: aplicar drag hacia 0
            currentVelX = Mathf.MoveTowards(currentVelX, 0f, airDrag * dt);
        }

        rb.velocity = new Vector2(currentVelX, rb.velocity.y);
        // ---------------------------------------

        ApplyJumpHoldBoostInFixed();
        ApplyMarioStyleGravities();
    }

    #region Input & Facing

    private void ReadHorizontalInput() {
        bool leftHeld = Input.GetKey(KeyCode.A);
        bool rightHeld = Input.GetKey(KeyCode.D);

        if (Input.GetKeyDown(KeyCode.A)) lastPressedDir = -1;
        if (Input.GetKeyDown(KeyCode.D)) lastPressedDir = 1;

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
        if (moveDir != 0) {
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

    #region Ground Check (dual raycasts)

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

    #endregion

    #region Jump (Immediate press + hold + Coyote + Buffer)

    private void HandleJumpInput_ImmediatePressHold(bool jumpDown, bool jumpUp) {
        bool canGroundLikeJumpNow = isGrounded || (coyoteTimer > 0f);

        // 1) Consumir buffer si se puede saltar “como suelo”
        if (jumpBufferTimer > 0f && canGroundLikeJumpNow) {
            DoGroundLikeJump();
            jumpBufferTimer = 0f;
            return;
        }

        // 2) Pulsación directa
        if (jumpDown) {
            if (canGroundLikeJumpNow) {
                jumpBufferTimer = 0f;
                DoGroundLikeJump();
                return;
            }

            // Doble salto (requiere pulsación directa)
            if (!isGrounded && canDoubleJump) {
                DoDoubleJump();
                return;
            }
        }

        // 3) Soltar tecla corta el hold
        if (jumpUp) {
            isJumpingHoldPhase = false;
        }
    }

    private void DoGroundLikeJump() {
        rb.velocity = new Vector2(rb.velocity.x, 0f);
        wasFalling = false;
        fallTimer = 0f;
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

            rb.velocity += Vector2.up * Physics2D.gravity.y * (currentMultiplier - 1f) * Time.fixedDeltaTime;
        }
        else {
            wasFalling = false;

            if (rb.velocity.y > 0f) {
                rb.velocity += Vector2.up * Physics2D.gravity.y * (riseGravityMultiplier - 1f) * Time.fixedDeltaTime;
            }
        }
    }

    #endregion

    #region Dash (8-way en aire, horizontal estricto en suelo, cargas independientes)

    private void HandleDashInput() {
        if (!Input.GetKeyDown(dashKey)) return;
        if (isDashing) return;

        int readyIdx = GetReadyDashIndex();
        if (readyIdx < 0) return; // no hay cargas listas

        Vector2 dir;

        if (isGrounded) {
            // En suelo: SOLO A o D (sin W/S, sin A+D, sin vacío)
            bool left = Input.GetKey(KeyCode.A);
            bool right = Input.GetKey(KeyCode.D);
            bool up = Input.GetKey(KeyCode.W);
            bool down = Input.GetKey(KeyCode.S);

            if (up || down) return;
            if (left == right) return;

            int hx = left ? -1 : 1;
            dir = new Vector2(hx, 0f);
        }
        else {
            // En aire: 8 direcciones
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
        }

        ConsumeDashCharge(readyIdx);
        StartCoroutine(DashRoutine(dir));
    }

    private IEnumerator DashRoutine(Vector2 dir) {
        isDashing = true;
        dashDir = dir;

        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;
        Vector2 originalVel = rb.velocity;
        rb.velocity = Vector2.zero;

        Vector2 targetPos = rb.position + dashDir * dashDistance;
        while ((rb.position - targetPos).sqrMagnitude > 0.0001f) {
            float step = dashSpeed * Time.fixedDeltaTime;
            Vector2 nextPos = Vector2.MoveTowards(rb.position, targetPos, step);
            rb.MovePosition(nextPos);
            yield return new WaitForFixedUpdate();
        }

        rb.gravityScale = originalGravity;
        rb.velocity = new Vector2(originalVel.x, 0f);

        wasFalling = false;
        fallTimer = 0f;
        fallContext = FallRampContext.FromDash;

        isDashing = false;
    }

    private void UpdateDashCharges(float dt, bool groundedNow) {
        int n = dashCooldownLeft.Length;
        for (int i = 0; i < n; i++) {
            if (dashReady[i]) continue;

            if (dashCooldownLeft[i] > 0f) {
                dashCooldownLeft[i] -= dt;
                if (dashCooldownLeft[i] <= 0f) {
                    dashCooldownLeft[i] = 0f;
                    if (groundedNow) {
                        dashReady[i] = true;
                        dashAwaitGround[i] = false;
                    }
                    else {
                        dashAwaitGround[i] = true;
                    }
                }
            }
            else {
                if (dashAwaitGround[i] && groundedNow) {
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

        Vector2 originL = (Vector2)transform.position + groundRayOffsetLeft;
        Vector2 originR = (Vector2)transform.position + groundRayOffsetRight;
        Gizmos.DrawLine(originL, originL + Vector2.down * groundRayLength);
        Gizmos.DrawLine(originR, originR + Vector2.down * groundRayLength);
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
