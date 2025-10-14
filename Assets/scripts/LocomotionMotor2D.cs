using UnityEngine;

[DisallowMultipleComponent]
public class LocomotionMotor2D : MonoBehaviour {
    [Header("Run/Acceleration")]
    public float baseSpeed = 6f;
    public float speedMultiplier = 1f;
    public float accel = 999f;
    public float airDrag = 5f;

    [Header("Apex Bonus")]
    public float apexVyThreshold = 1.0f;
    public float apexBonusSpeedMultiplier = 1.15f;
    public float apexBonusAccelMultiplier = 1.15f;

    [Header("Run Speed Gain")]
    public float runSpeedMin = 0f;  // 0 => use baseSpeed at Awake
    public float runSpeedMax = 8f;
    public float runGainPerSecond = 2f;
    public float runDecayPerSecond = 4f;

    Rigidbody2D rb;
    WallModule2D wall;
    Transform spriteChild;
    System.Func<bool> getFacingRight;
    System.Action<bool> setFacingRight;

    float currentVelX;
    float currentRunSpeed;
    float apexSpeedMultNow = 1f, apexAccelMultNow = 1f;

    public float CurrentHorizontalSpeed => currentVelX;

    public void InitSprite(Transform sprite, System.Func<bool> get, System.Action<bool> set) {
        spriteChild = sprite; getFacingRight = get; setFacingRight = set;
    }

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        wall = GetComponent<WallModule2D>();

        if (runSpeedMin <= 0f) runSpeedMin = baseSpeed;
        currentRunSpeed = Mathf.Max(runSpeedMin, baseSpeed);
        runSpeedMax = Mathf.Max(runSpeedMax, runSpeedMin);
    }

    public void TickHorizontal(int moveDir, bool holdLeft, bool holdRight) {
        // Facing (blocked by wall-jump lock handled in WallModule)
        if (moveDir != 0 && (!wall || wall.WallJumpLockTimer <= 0f)) {
            bool shouldFaceRight = moveDir > 0;
            if (getFacingRight != null && setFacingRight != null) {
                if (shouldFaceRight != getFacingRight()) {
                    setFacingRight(shouldFaceRight);
                    FlipSprite();
                }
            }
        }
    }

    public void FixedTick(Grounding2D grounding) {
        float dt = Time.fixedDeltaTime;

        UpdateApexMultipliers();
        UpdateGroundRunSpeed(dt, grounding.IsGrounded, grounding.HasHorizontalInput);

        float effectiveBaseSpeed = Mathf.Clamp(currentRunSpeed, runSpeedMin, runSpeedMax);
        float speedWithApex = effectiveBaseSpeed * speedMultiplier * apexSpeedMultNow;
        float accelWithApex = accel * apexAccelMultNow;
        float targetSpeed = grounding.HorizontalIntent * speedWithApex;

        // Wall jump lock holds X (from WallModule)
        if (wall && wall.WallJumpLockTimer > 0f) {
            rb.linearVelocity = new Vector2(currentVelX, rb.linearVelocity.y);
        }
        else if (grounding.IsGrounded || grounding.HasHorizontalInput) {
            currentVelX = Mathf.MoveTowards(currentVelX, targetSpeed, accelWithApex * dt);
            rb.linearVelocity = new Vector2(currentVelX, rb.linearVelocity.y);
        }
        else {
            currentVelX = Mathf.MoveTowards(currentVelX, 0f, airDrag * dt);
            rb.linearVelocity = new Vector2(currentVelX, rb.linearVelocity.y);
        }

        // Static stick when grabbing wall (handled by WallModule flag)
        if (wall && wall.IsWallGrabbing && !wall.IsWallSliding && !grounding.DropBlockWallGrabActive) {
            rb.linearVelocity = Vector2.zero;
        }
    }

    void UpdateApexMultipliers() {
        float vy = rb.linearVelocity.y;
        float thr = Mathf.Max(0.0001f, apexVyThreshold);
        float nearApex01 = 1f - Mathf.Clamp01(Mathf.Abs(vy) / thr);

        apexSpeedMultNow = Mathf.Lerp(1f, Mathf.Max(1f, apexBonusSpeedMultiplier), nearApex01);
        apexAccelMultNow = Mathf.Lerp(1f, Mathf.Max(1f, apexBonusAccelMultiplier), nearApex01);
    }

    void UpdateGroundRunSpeed(float dt, bool grounded, bool hasInput) {
        if (grounded && hasInput)
            currentRunSpeed = Mathf.MoveTowards(currentRunSpeed, runSpeedMax, runGainPerSecond * dt);
        else
            currentRunSpeed = Mathf.MoveTowards(currentRunSpeed, runSpeedMin, runDecayPerSecond * dt);
    }

    public void SetCurrentVelX(float v) {
        currentVelX = v;
        // opcionalmente tambi�n reflejarla ya mismo en rb:
        var rb = GetComponent<Rigidbody2D>();
        if (rb) rb.linearVelocity = new Vector2(currentVelX, rb.linearVelocity.y);
    }


    void FlipSprite() {
        if (!spriteChild) return;
        var s = spriteChild.localScale;
        s.x *= -1f;
        spriteChild.localScale = s;
    }



}
