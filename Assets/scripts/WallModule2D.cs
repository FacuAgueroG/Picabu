using UnityEngine;

[DisallowMultipleComponent]
public class WallModule2D : MonoBehaviour {
    public enum WallSide { None, Left, Right }

    [Header("Wall Check (dual per side)")]
    public Vector2 wallLeftOffsetA = new Vector2(-0.5f, 0.2f);
    public Vector2 wallLeftOffsetB = new Vector2(-0.5f, -0.2f);
    public Vector2 wallRightOffsetA = new Vector2(0.5f, 0.2f);
    public Vector2 wallRightOffsetB = new Vector2(0.5f, -0.2f);
    public float wallRayLength = 0.12f;
    public float wallSkinPush = 0.02f;
    public LayerMask groundMask;
    public string groundTag = "Ground";

    [Header("Wall Grab / Slide / Jump")]
    public bool enableWallGrab = true;
    public float wallGrabHoldTime = 0.2f;
    public float wallSlideGravityMultiplier = 0.6f;
    public float wallRegrabCooldown = 0.15f;
    public float wallJumpVerticalForce = 10f;
    public float wallJumpHorizontalLaunchSpeed = 10f;
    public float wallJumpOppositeMultiplier = 1.3f;
    public float wallJumpLockTime = 0.12f;
    public bool wallCountsAsGroundForDash = true;

    [Header("Conditions")]
    public bool requireFallingForWallGrab = true;
    public float wallGrabFallVyThreshold = -0.001f;
    public bool disableWallGrabWhenBothSidesHit = true;
    public bool useTemporalBothSidesWindow = true;
    public float bothSidesWindow = 0.06f;

    [Header("Input Suppress After WallJump")]
    public float wallInputSuppressTime = 0.4f;

    // State
    public bool IsOnWall { get; private set; }
    public bool IsWallGrabbing { get; private set; }
    public bool IsWallSliding { get; private set; }
    public WallSide Side { get; private set; } = WallSide.None;

    public float WallJumpLockTimer { get; private set; } = 0f;

    // Internal
    Rigidbody2D rb;
    PlayerBrain brain;
    Grounding2D grounding;
    JumpModule2D jump;
    LocomotionMotor2D motor; // <-- NUEVO

    float wallGrabTimer = 0f;
    float wallRegrabTimer = 0f;
    float leftHitRecentTimer = 0f;
    float rightHitRecentTimer = 0f;

    float wallInputSuppressTimer = 0f;
    WallSide suppressedWallSide = WallSide.None;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        brain = GetComponent<PlayerBrain>();
        grounding = GetComponent<Grounding2D>();
        jump = GetComponent<JumpModule2D>();
        motor = GetComponent<LocomotionMotor2D>(); // <-- NUEVO
    }

    public bool IsGroundLikeForDash => grounding.IsGrounded || (IsOnWall && wallCountsAsGroundForDash);

    public bool IsInputSuppressedTo(WallSide s) => wallInputSuppressTimer > 0f && suppressedWallSide == s;

    public void TickSense() {
        // timers
        if (wallInputSuppressTimer > 0f) {
            wallInputSuppressTimer -= Time.deltaTime;
            if (wallInputSuppressTimer <= 0f) { wallInputSuppressTimer = 0f; suppressedWallSide = WallSide.None; }
        }
        if (wallRegrabTimer > 0f) wallRegrabTimer -= Time.deltaTime;
        if (WallJumpLockTimer > 0f) WallJumpLockTimer -= Time.deltaTime;

        if (!enableWallGrab || grounding.DropBlockWallGrabActive) {
            ClearWallStates();
            return;
        }

        // 4 rays per side
        bool leftA = CastWallAtDetailed(wallLeftOffsetA, Vector2.left, out _);
        bool leftB = CastWallAtDetailed(wallLeftOffsetB, Vector2.left, out _);
        bool rightA = CastWallAtDetailed(wallRightOffsetA, Vector2.right, out _);
        bool rightB = CastWallAtDetailed(wallRightOffsetB, Vector2.right, out _);

        bool left = leftA || leftB;
        bool right = rightA || rightB;

        if (useTemporalBothSidesWindow) {
            if (left) leftHitRecentTimer = bothSidesWindow;
            if (right) rightHitRecentTimer = bothSidesWindow;
            if (leftHitRecentTimer > 0f) leftHitRecentTimer -= Time.deltaTime;
            if (rightHitRecentTimer > 0f) rightHitRecentTimer -= Time.deltaTime;
        }
        bool bothSidesHitRecent = useTemporalBothSidesWindow
            ? (leftHitRecentTimer > 0f && rightHitRecentTimer > 0f)
            : (left && right);

        WallSide newSide = WallSide.None;
        if (left && !right) newSide = WallSide.Left;
        else if (right && !left) newSide = WallSide.Right;
        else if (left && right) newSide = (brain.moveX >= 0) ? WallSide.Right : WallSide.Left;

        bool touchingWall = (newSide != WallSide.None);
        bool canAttach = touchingWall && !grounding.IsGrounded && CanWallAttachNow();
        if (disableWallGrabWhenBothSidesHit && bothSidesHitRecent) canAttach = false;

        bool wasOnWall = IsOnWall;
        WallSide prevSide = Side;

        IsOnWall = canAttach;
        Side = IsOnWall ? newSide : WallSide.None;

        if (IsOnWall) {
            if (!wasOnWall || (prevSide != Side)) {
                if (wallRegrabTimer <= 0f) EnterWallGrab();
            }

            if (IsWallGrabbing && !IsWallSliding) {
                wallGrabTimer += Time.deltaTime;
                if (wallGrabTimer >= wallGrabHoldTime) EnterWallSlide();
            }
        }
        else {
            if (wasOnWall) {
                ExitWallStates();
                wallRegrabTimer = wallRegrabCooldown;
            }
        }
    }

    public void DoWallJump() {
        if (!IsOnWall) return;

        var prevWallSide = Side;
        int away = (prevWallSide == WallSide.Right) ? -1 : +1;

        bool towardWall = (prevWallSide == WallSide.Left && brain.holdLeft)
                       || (prevWallSide == WallSide.Right && brain.holdRight);

        bool inputOpposite = (away < 0 && brain.holdLeft) || (away > 0 && brain.holdRight);
        float launchX = wallJumpHorizontalLaunchSpeed * (inputOpposite ? wallJumpOppositeMultiplier : 1f);

        // Reset & exit wall states
        rb.linearVelocity = Vector2.zero;
        ExitWallStates();

        // ---- LANZAMIENTO: INYECTAR X EN EL MOTOR PARA QUE NO LA PISE ----
        float launchVX = away * launchX;
        if (motor) motor.SetCurrentVelX(launchVX);
        rb.linearVelocity = new Vector2(launchVX, 0f);

        // Vertical impulse
        if (jump) jump.SetFallContext(JumpModule2D.FallContext.FromJump);
        rb.AddForce(Vector2.up * wallJumpVerticalForce, ForceMode2D.Impulse);

        // Start hold phase like normal jump
        if (jump) jump.BeginHoldPhaseFromWall();

        // Lock y regrab cooldown
        WallJumpLockTimer = Mathf.Max(0f, wallJumpLockTime);
        wallRegrabTimer = wallRegrabCooldown;

        // Supresi�n de input hacia la pared si correspond�a
        if (towardWall) {
            suppressedWallSide = prevWallSide;
            wallInputSuppressTimer = Mathf.Max(0f, wallInputSuppressTime);
        }

        // Orientar sprite hacia afuera
        if (away > 0 && !brain.facingRight) { brain.facingRight = true; FlipSprite(); }
        else if (away < 0 && brain.facingRight) { brain.facingRight = false; FlipSprite(); }
    }

    bool CanWallAttachNow() {
        if (!requireFallingForWallGrab) return true;
        return rb && rb.linearVelocity.y <= wallGrabFallVyThreshold;
    }

    bool CastWallAtDetailed(Vector2 localOffset, Vector2 dir, out Collider2D hitCol) {
        Vector2 origin = (Vector2)transform.position + localOffset;

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, wallRayLength, groundMask);
        if (hit.collider != null) { hitCol = hit.collider; return true; }

        RaycastHit2D hitNoMask = Physics2D.Raycast(origin, dir, wallRayLength);
        if (hitNoMask.collider != null && hitNoMask.collider.CompareTag(groundTag)) { hitCol = hitNoMask.collider; return true; }

        hitCol = null; return false;
    }

    void EnterWallGrab() {
        IsWallGrabbing = true; IsWallSliding = false; wallGrabTimer = 0f;

        float push = wallSkinPush;
        if (Side == WallSide.Left) transform.position += new Vector3(+push, 0f, 0f);
        if (Side == WallSide.Right) transform.position += new Vector3(-push, 0f, 0f);

        // touching wall grants double jump again
        if (jump) jump.CanDoubleJump = true;
    }

    void EnterWallSlide() { IsWallGrabbing = false; IsWallSliding = true; }
    public void ExitWallStates() { IsWallGrabbing = false; IsWallSliding = false; wallGrabTimer = 0f; Side = WallSide.None; }
    void ClearWallStates() { IsOnWall = false; ExitWallStates(); }

    void FlipSprite() {
        if (!brain || !brain.spriteChild) return;
        var s = brain.spriteChild.localScale;
        s.x *= -1f;
        brain.spriteChild.localScale = s;
    }
}
