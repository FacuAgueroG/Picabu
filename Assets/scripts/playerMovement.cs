using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
public class Player2DController : MonoBehaviour {
    [Header("Movement")]
    [SerializeField] private float baseSpeed = 6f;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float accel = 999f;
    [SerializeField] private bool faceRightDefault = true;

    [Header("Mario-style Jump")]
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float riseGravityMultiplier = 1.0f;
    [SerializeField] private float fallRampTime = 0.15f; // tiempo para llegar al multiplicador total

    [Header("Jump (Press-to-Jump + Hold-to-Height)")]
    [SerializeField] private float jumpMinForce = 6f;
    [SerializeField] private float jumpMaxForce = 14f;
    [SerializeField] private float jumpMaxChargeTime = 0.35f;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;

    [Header("Ground Check (Raycast)")]
    [SerializeField] private Vector2 groundRayOffset = new Vector2(0f, -0.5f);
    [SerializeField] private float groundRayLength = 0.2f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private string groundTag = "Ground";

    [Header("Dash (8 Direcciones)")]
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashDistance = 6f;
    [SerializeField] private int dashMaxCharges = 2;
    [SerializeField] private float dashCooldown = 1.25f;

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

    // Dash movement state
    private bool isDashing = false;
    private Vector2 dashDir = Vector2.zero;

    // Cargas independientes de dash
    private float[] dashCooldownLeft;
    private bool[] dashReady;
    private bool[] dashAwaitGround;

    // Fall ramp control
    private float fallTimer = 0f;
    private bool wasFalling = false;

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

        isGrounded = CheckGrounded();
        if (isGrounded) {
            canDoubleJump = true;
        }

        jumpHeld = Input.GetKey(jumpKey);

        HandleJumpInput_ImmediatePressHold();
        HandleDashInput();

        UpdateDashCharges(Time.deltaTime, isGrounded);
    }

    private void FixedUpdate() {
        if (isDashing) return;

        float targetSpeed = moveDir * baseSpeed * speedMultiplier;
        currentVelX = Mathf.MoveTowards(currentVelX, targetSpeed, accel * Time.fixedDeltaTime);

        rb.velocity = new Vector2(currentVelX, rb.velocity.y);

        ApplyJumpHoldBoostInFixed();
        ApplyMarioStyleGravities();
    }

    #region Input & Movement

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
            facingRight = moveDir > 0;
        }
    }

    #endregion

    #region Ground Check

    private bool CheckGrounded() {
        Vector2 origin = (Vector2)transform.position + groundRayOffset;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundRayLength, groundMask);

        if (hit.collider != null) return true;

        RaycastHit2D hitNoMask = Physics2D.Raycast(origin, Vector2.down, groundRayLength);
        if (hitNoMask.collider != null && hitNoMask.collider.CompareTag(groundTag))
            return true;

        return false;
    }

    #endregion

    #region Jump (Immediate press + hold to reach max)

    private void HandleJumpInput_ImmediatePressHold() {
        if (Input.GetKeyDown(jumpKey)) {
            if (isGrounded || canDoubleJump) {
                bool usingDoubleJump = (!isGrounded && canDoubleJump);

                if (usingDoubleJump) {
                    rb.velocity = new Vector2(rb.velocity.x, 0f);
                    canDoubleJump = false;
                }

                float riseScale = Mathf.Sqrt(Mathf.Max(0.01f, riseGravityMultiplier));

                float initialImpulse = jumpMinForce * riseScale;
                rb.AddForce(Vector2.up * initialImpulse, ForceMode2D.Impulse);

                float extraTotal = Mathf.Max(0f, (jumpMaxForce - jumpMinForce) * riseScale);
                jumpExtraImpulseLeft = extraTotal;

                jumpExtraImpulsePerSec = (jumpMaxChargeTime > 0f)
                    ? (extraTotal / jumpMaxChargeTime)
                    : (extraTotal / Mathf.Epsilon);

                isJumpingHoldPhase = true;
                jumpHoldTimer = 0f;
            }
        }

        if (Input.GetKeyUp(jumpKey)) {
            isJumpingHoldPhase = false;
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
        if (rb.velocity.y < 0f) {
            // Transición a caída: asegurar inicio consistente
            if (!wasFalling) {
                wasFalling = true;
                fallTimer = 0f;
                // arrancar la caída SIEMPRE desde vel.y = 0
                rb.velocity = new Vector2(rb.velocity.x, 0f);
            }
            else {
                fallTimer += Time.fixedDeltaTime;
            }

            // multiplicador progresivo 1 -> fallGravityMultiplier
            float t = (fallRampTime > 0f) ? Mathf.Clamp01(fallTimer / fallRampTime) : 1f;
            float currentMultiplier = Mathf.Lerp(1f, fallGravityMultiplier, t);

            rb.velocity += Vector2.up * Physics2D.gravity.y * (currentMultiplier - 1f) * Time.fixedDeltaTime;
        }
        else {
            // No cayendo (subiendo o v.y == 0)
            wasFalling = false;

            if (rb.velocity.y > 0f) {
                rb.velocity += Vector2.up * Physics2D.gravity.y * (riseGravityMultiplier - 1f) * Time.fixedDeltaTime;
            }
        }
    }

    #endregion

    #region Dash (8-way, distancia exacta + cargas independientes con cooldown + suelo requerido)
    private void HandleDashInput() {
        if (!Input.GetKeyDown(dashKey)) return;
        if (isDashing) return;

        int readyIdx = GetReadyDashIndex();
        if (readyIdx < 0) return; // no hay cargas listas

        Vector2 dir;

        if (isGrounded) {
            // En suelo: solo A o D solos (sin W/S, sin A+D, sin vacío)
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

        // Restaurar gravedad y garantizar inicio de caída consistente
        rb.gravityScale = originalGravity;

        // No restaurar vel.y previa (podía ser negativa). Mantener vel.y = 0
        // Restauramos solo la componente horizontal (sensación más natural).
        rb.velocity = new Vector2(originalVel.x, 0f);

        // Resetear estado de caída para que el ramp arranque fresco
        wasFalling = false;
        fallTimer = 0f;

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
        Vector2 origin = (Vector2)transform.position + groundRayOffset;
        Gizmos.DrawLine(origin, origin + Vector2.down * groundRayLength);
    }

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
