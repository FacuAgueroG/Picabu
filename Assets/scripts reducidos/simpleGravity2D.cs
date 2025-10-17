using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class simpleGravity2D : MonoBehaviour {
    public simpleControls controls;    // Para leer JumpHeld()
    Rigidbody2D rb;

    [Header("Integración opcional")]
    [Tooltip("Si se referencia, la gravedad respetará stalls verticales.")]
    public simpleAirStall airStall;    // (opcional)

    [Header("Gravedades")]
    public float upGravityScale = 1.0f;
    public float downGravityScaleStart = 2.0f;
    public float downGravityScaleMax = 4.0f;

    [Header("Rampa de caída")]
    public float fallRampTime = 0.20f;

    [Header("Corte de salto al soltar (en SUBIDA)")]
    public bool hardCutOnRelease = true;
    public float cutUpwardSpeed = 0f;
    public float fallKickSpeed = 0f;
    public float jumpCutMultiplier = 2.0f;

    [Header("Límites")]
    public float maxFallSpeed = 25f;

    bool suppressCutUntilFall = false;
    float fallTimer = 0f;
    float prevVy = 0f;

    public void SuppressCutUntilFall() => suppressCutUntilFall = true;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        if (airStall == null) airStall = GetComponent<simpleAirStall>();
    }

    void FixedUpdate() {
        // Respetar stall vertical
        if (airStall != null && airStall.IsStalling) {
            Vector2 v = rb.linearVelocity;
            if (v.y != 0f) {
                v.y = 0f;
                rb.linearVelocity = v;
            }
            rb.gravityScale = 0f;
            prevVy = 0f;
            return;
        }

        Vector2 vel = rb.linearVelocity;

        bool startedFallingThisFrame = (prevVy >= 0f && vel.y < 0f);
        if (startedFallingThisFrame) {
            fallTimer = 0f;
            suppressCutUntilFall = false;
        }

        if (vel.y > 0f) // SUBIENDO
        {
            fallTimer = 0f;

            if (!suppressCutUntilFall && controls != null && !controls.JumpHeld()) {
                if (hardCutOnRelease) {
                    if (vel.y > cutUpwardSpeed) vel.y = cutUpwardSpeed;
                    if (fallKickSpeed > 0f) vel.y = -Mathf.Abs(fallKickSpeed);
                    rb.linearVelocity = vel;
                    rb.gravityScale = downGravityScaleStart;
                }
                else {
                    rb.gravityScale = upGravityScale * jumpCutMultiplier;
                }
            }
            else {
                rb.gravityScale = upGravityScale;
            }
        }
        else if (vel.y < 0f) // CAYENDO
        {
            if (fallRampTime <= 0f) {
                rb.gravityScale = downGravityScaleMax;
            }
            else {
                fallTimer += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(fallTimer / fallRampTime);
                float eased = t * t;
                rb.gravityScale = Mathf.Lerp(downGravityScaleStart, downGravityScaleMax, eased);
            }

            if (vel.y < -maxFallSpeed) {
                vel.y = -maxFallSpeed;
                rb.linearVelocity = vel;
            }
        }
        else { // ÁPICE
            rb.gravityScale = upGravityScale;
        }

        prevVy = vel.y;
    }
}
