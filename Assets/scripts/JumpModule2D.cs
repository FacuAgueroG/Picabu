using UnityEngine;

[DisallowMultipleComponent]
public class JumpModule2D : MonoBehaviour {
    public enum FallContext { None, FromJump, FromDash }

    [Header("Press + Hold-to-Height")]
    public float jumpMinForce = 6f;
    public float jumpMaxForce = 14f;
    public float jumpMaxChargeTime = 0.35f;
    public float riseGravityMultiplier = 1.0f;

    [Header("Coyote & Jump Buffer")]
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.12f;

    [Header("Dash Jump Buffer (consume on dash end)")]
    public bool enableDashJumpBuffer = true;
    public float dashJumpBufferTime = 0.1f;

    Rigidbody2D rb;
    PlayerBrain brain;
    Grounding2D grounding;
    WallModule2D wall;

    // runtime
    public bool CanDoubleJump { get; set; } = false;

    float coyoteTimer = 0f;
    float jumpBufferTimer = 0f;
    float dashJumpBufferTimer = 0f;

    bool isJumpingHoldPhase = false;
    bool jumpHeldFrame = false;
    float jumpHoldTimer = 0f;
    float jumpExtraImpulseLeft = 0f;
    float jumpExtraImpulsePerSec = 0f;

    public float CurrentVelX { get; set; } = 0f;
    public FallContext CurrentFallContext { get; private set; } = FallContext.None;
    public bool ExecutedJumpThisFrame { get; private set; } = false;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        brain = GetComponent<PlayerBrain>();
        grounding = GetComponent<Grounding2D>();
        wall = GetComponent<WallModule2D>();
    }

    public void SetFallContext(FallContext ctx) => CurrentFallContext = ctx;

    public void HandleJumpInput(bool jumpDown, bool jumpUp, bool jumpHeld) {
        ExecutedJumpThisFrame = false;
        jumpHeldFrame = jumpHeld;

        // coyote refresh
        if (grounding.IsGrounded) {
            CanDoubleJump = true;
            coyoteTimer = coyoteTime;
            CurrentFallContext = FallContext.None;
        }
        else if (coyoteTimer > 0f) coyoteTimer -= Time.deltaTime;

        // Buffer setup (avoid creating while dashing downward slam; dash module controls that flag on its side)
        if (jumpDown) {
            bool dropIntent = brain.holdDown;

            // Drop-through on one-way (S+Space) blocks jump this frame
            if (dropIntent && grounding.IsGrounded && grounding.IsOneWay(grounding.LastGroundCollider)) {
                bool consumed = grounding.TryDropThroughNow();
                if (consumed) { ExecutedJumpThisFrame = true; return; }
            }

            // 1) Armado del buffer de salto durante dash
            if (enableDashJumpBuffer && JumpModule2D.DashActiveOn(this))
                dashJumpBufferTimer = dashJumpBufferTime;

            // 2) Buffer normal solo si NO est�s dashing
            if (!JumpModule2D.DashActiveOn(this)) {
                if (!dropIntent || wall.IsOnWall || grounding.IsGrounded)
                    jumpBufferTimer = jumpBufferTime;
            }

        }
        else if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;

        // Wall-jump priority if on wall (buffer respected)
        if ((wall.IsWallGrabbing || wall.IsWallSliding) && (jumpDown || (jumpBufferTimer > 0f))) {
            wall.DoWallJump();
            jumpBufferTimer = 0f;
            ExecutedJumpThisFrame = true;
            return;
        }

        bool canGroundLike = grounding.IsGrounded || (coyoteTimer > 0f);

        if (jumpBufferTimer > 0f && canGroundLike) {
            DoGroundLikeJump();
            jumpBufferTimer = 0f;
            ExecutedJumpThisFrame = true;
            return;
        }

        if (jumpDown) {
            if (canGroundLike) {
                jumpBufferTimer = 0f;
                DoGroundLikeJump();
                ExecutedJumpThisFrame = true;
                return;
            }
            if (!grounding.IsGrounded && CanDoubleJump) {
                DoDoubleJump();
                ExecutedJumpThisFrame = true;
                return;
            }
        }

        if (jumpUp) isJumpingHoldPhase = false;
    }

    public void FixedTickHold() {
        if (!isJumpingHoldPhase) return;
        if (!jumpHeldFrame || jumpHoldTimer >= jumpMaxChargeTime || jumpExtraImpulseLeft <= 0f || rb.linearVelocity.y <= 0f) {
            isJumpingHoldPhase = false;
            return;
        }

        float dt = Time.fixedDeltaTime;
        float add = Mathf.Min(jumpExtraImpulsePerSec * dt, jumpExtraImpulseLeft);
        rb.AddForce(Vector2.up * add, ForceMode2D.Impulse);
        jumpExtraImpulseLeft -= add;
        jumpHoldTimer += dt;

        if (jumpExtraImpulseLeft <= 0f || jumpHoldTimer >= jumpMaxChargeTime) isJumpingHoldPhase = false;
    }

    // ---- public hooks for DashModule ----
    public bool TryConsumeDashJumpBuffer() {
        if (!enableDashJumpBuffer || dashJumpBufferTimer <= 0f) return false;

        // If trying to drop through on one-way, don't consume
        if (grounding.IsGrounded && brain.holdDown && grounding.IsOneWay(grounding.LastGroundCollider)) return false;

        dashJumpBufferTimer = 0f;

        if (grounding.IsGrounded || coyoteTimer > 0f) { DoGroundLikeJump(); return true; }
        if (!grounding.IsGrounded && CanDoubleJump) { DoDoubleJump(); return true; }
        return false;
    }

    public void BeginHoldPhaseFromWall() {
        float riseScale = Mathf.Sqrt(Mathf.Max(0.01f, riseGravityMultiplier));
        float extraTotal = Mathf.Max(0f, (jumpMaxForce - jumpMinForce) * riseScale);
        jumpExtraImpulseLeft = extraTotal;
        jumpExtraImpulsePerSec = (jumpMaxChargeTime > 0f) ? (extraTotal / jumpMaxChargeTime) : (extraTotal / Mathf.Epsilon);
        isJumpingHoldPhase = true;
        jumpHoldTimer = 0f;
    }

    // ---- internals ----
    void BeginJumpCommon(bool consumeDouble) {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        if (wall) wall.ExitWallStates();
        CurrentFallContext = FallContext.FromJump;

        if (consumeDouble) CanDoubleJump = false;

        float riseScale = Mathf.Sqrt(Mathf.Max(0.01f, riseGravityMultiplier));
        float initialImpulse = jumpMinForce * riseScale;
        rb.AddForce(Vector2.up * initialImpulse, ForceMode2D.Impulse);

        float extraTotal = Mathf.Max(0f, (jumpMaxForce - jumpMinForce) * riseScale);
        jumpExtraImpulseLeft = extraTotal;
        jumpExtraImpulsePerSec = (jumpMaxChargeTime > 0f) ? (extraTotal / jumpMaxChargeTime) : (extraTotal / Mathf.Epsilon);

        isJumpingHoldPhase = true;
        jumpHoldTimer = 0f;
    }

    void DoGroundLikeJump() => BeginJumpCommon(false);
    void DoDoubleJump() => BeginJumpCommon(true);

    public void DoDownwardCancelJump(float flatImpulse) {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        if (wall) wall.ExitWallStates();
        CurrentFallContext = FallContext.FromJump;

        float impulse = Mathf.Max(0f, flatImpulse);
        rb.AddForce(Vector2.up * impulse, ForceMode2D.Impulse);

        isJumpingHoldPhase = false;
        jumpExtraImpulseLeft = 0f;
        jumpHoldTimer = 0f;
    }

    public static bool DashActiveOn(JumpModule2D j) {
        var d = j ? j.GetComponent<DashModule2D>() : null;
        return d && d.IsDashing;
    }
}
