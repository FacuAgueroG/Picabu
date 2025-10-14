using UnityEngine;

[DisallowMultipleComponent]
public class GravityModule2D : MonoBehaviour {
    [Header("Mario-style Gravity")]
    public float fallGravityMultiplier = 2.5f;
    public float riseGravityMultiplier = 1.0f;
    public float fallRampTimeJump = 0.15f;
    public float fallRampTimeDash = 0.15f;

    Rigidbody2D rb;
    WallModule2D wall;
    JumpModule2D jump;
    DashModule2D dash;

    bool wasFalling = false;
    float fallTimer = 0f;

    public void TickModes() { /* placeholder for future mode gates */ }

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        wall = GetComponent<WallModule2D>();
        jump = GetComponent<JumpModule2D>();
        dash = GetComponent<DashModule2D>();
    }

    public void FixedTick() {
        if (dash && dash.IsDownwardHoldDash) return; // handled inside dash
        // (legacy slam path removed; use downward hold dash)

        // Wall slide reduced gravity
        if (wall && wall.IsWallSliding) {
            float mult = Mathf.Max(0f, wall.wallSlideGravityMultiplier);
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * mult * Time.fixedDeltaTime;
            return;
        }

        if (rb.linearVelocity.y < 0f) {
            if (!wasFalling) {
                wasFalling = true;
                fallTimer = 0f;
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
            }
            else fallTimer += Time.fixedDeltaTime;

            float ramp = (jump.CurrentFallContext == JumpModule2D.FallContext.FromDash) ? fallRampTimeDash : fallRampTimeJump;
            float t = (ramp > 0f) ? Mathf.Clamp01(fallTimer / ramp) : 1f;
            float multNow = Mathf.Lerp(1f, fallGravityMultiplier, t);

            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * multNow * Time.fixedDeltaTime;
        }
        else {
            wasFalling = false;
            if (rb.linearVelocity.y > 0f) {
                rb.linearVelocity += Vector2.up * Physics2D.gravity.y * Mathf.Max(0f, riseGravityMultiplier) * Time.fixedDeltaTime;
            }
        }
    }
}
