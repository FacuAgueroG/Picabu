using UnityEngine;

/// <summary>
/// Gravedad del ENEMIGO con:
/// - Rampa de caída (downStart -> downMax)
/// - Mini-rampa en el ápice (apex cushion)
/// - Respeto de air-stall (simpleEnemyAirStall)
/// - Modo post-stall: si se activó al menos una vez por Ctrl+S, cae con Max hasta tocar suelo.
/// - Utilidades Height/Time (ConfigureUpGravityFrom, ComputeInitialSpeed)
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class simpleEnemyGravity2D : MonoBehaviour {
    Rigidbody2D rb;

    [Header("Gravedades")]
    public float upGravityScale = 1.0f;
    public float downGravityScaleStart = 2.0f;
    public float downGravityScaleMax = 4.0f;

    [Header("Rampa de caída principal")]
    public float fallRampTime = 0.20f;

    [Header("Límites")]
    public float maxFallSpeed = 25f;

    [Header("Apex Cushion (mini-rampa en el ápice)")]
    public bool enableApexCushion = true;
    [Min(0f)] public float apexCushionTime = 0.10f;
    [Min(0f)] public float apexCushionStartScale = 1.5f;
    [Min(0f)] public float apexCushionEndScale = 2.0f;
    [Min(0f)] public float apexVyThreshold = 0.20f;
    public float apexCushionMaxDownSpeed = 2.5f;
    [Min(0f)] public float apexCushionCooldown = 0.15f;

    [Header("Integraciones")]
    public simpleEnemyAirStall airStall;       // opcional
    public simpleEnemyState state;             // para saber cuándo toca suelo y resetear el modo forzado

    // ===== Post-stall: forzar Max hasta suelo =====
    bool forceMaxFallUntilGround = false;

    float fallTimer = 0f;
    float prevVy = 0f;

    bool inApexCushion = false;
    float cushionTimer = 0f;
    float lastCushionAt = -999f;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        if (airStall == null) airStall = GetComponent<simpleEnemyAirStall>();
        if (state == null) state = GetComponent<simpleEnemyState>();
    }

    /// <summary>
    /// Llamado por simpleEnemyAirStall cuando el enemigo recibió al menos un Ctrl+S con stall.
    /// Activa un modo que, al salir del stall, ignora cushion y rampa y aplica downGravityScaleMax
    /// hasta que el enemigo vuelve a tocar el suelo.
    /// </summary>
    public void ForceMaxFallUntilGround() {
        forceMaxFallUntilGround = true;
        // Reset de perfiles para evitar residuos
        inApexCushion = false;
        cushionTimer = 0f;
        fallTimer = 0f;
    }

    void FixedUpdate() {
        // Si tocó el suelo, desactivamos el modo forzado
        if (state != null && state.airState == EnemyAirState.OnGround) {
            forceMaxFallUntilGround = false;
        }

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

        // Cushion normal solo si no estamos en modo forzado
        if (!forceMaxFallUntilGround && enableApexCushion && startedFallingThisFrame) {
            bool notInCooldown = (Time.time - lastCushionAt) >= apexCushionCooldown;
            bool nearApex = Mathf.Abs(vel.y) <= apexVyThreshold;
            if (notInCooldown && nearApex) {
                inApexCushion = true;
                cushionTimer = 0f;
                lastCushionAt = Time.time;
                fallTimer = 0f;
            }
        }

        if (vel.y > 0f) {
            // SUBIENDO
            inApexCushion = false;
            cushionTimer = 0f;
            fallTimer = 0f;
            rb.gravityScale = upGravityScale;
        }
        else if (vel.y < 0f) {
            // CAYENDO
            if (forceMaxFallUntilGround) {
                // 🚩 Modo forzado: siempre Max hasta suelo (sin cushion, sin rampa)
                inApexCushion = false;
                cushionTimer = 0f;
                fallTimer = 0f;
                rb.gravityScale = downGravityScaleMax;
            }
            else {
                if (inApexCushion) {
                    cushionTimer += Time.fixedDeltaTime;
                    float t = Mathf.Clamp01(cushionTimer / Mathf.Max(0.0001f, apexCushionTime));
                    float eased = SmoothStep(t);
                    rb.gravityScale = Mathf.Lerp(apexCushionStartScale, apexCushionEndScale, eased);

                    if (apexCushionMaxDownSpeed > 0f && vel.y < -apexCushionMaxDownSpeed) {
                        vel.y = -apexCushionMaxDownSpeed;
                        rb.linearVelocity = vel;
                    }

                    if (cushionTimer >= apexCushionTime) {
                        inApexCushion = false;
                        cushionTimer = 0f;
                        fallTimer = 0f;
                    }
                }
                else {
                    if (fallRampTime <= 0f) {
                        rb.gravityScale = downGravityScaleMax;
                    }
                    else {
                        fallTimer += Time.fixedDeltaTime;
                        float t = Mathf.Clamp01(fallTimer / fallRampTime);
                        float eased = t * t;
                        rb.gravityScale = Mathf.Lerp(downGravityScaleStart, downGravityScaleMax, eased);
                    }
                }
            }

            if (vel.y < -maxFallSpeed) {
                vel.y = -maxFallSpeed;
                rb.linearVelocity = vel;
            }
        }
        else {
            // ÁPICE exacto (vy == 0)
            if (forceMaxFallUntilGround) {
                // ✅ FIX: asegurar gravedad máxima para que empiece a caer en el próximo Fixed
                rb.gravityScale = downGravityScaleMax;
            }
            else if (!inApexCushion) {
                rb.gravityScale = upGravityScale;
            }
            fallTimer = 0f;
        }

        prevVy = rb.linearVelocity.y;
    }

    static float SmoothStep(float t) {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    public void ConfigureUpGravityFrom(float apexHeight, float timeToApex) {
        float gWorld = Mathf.Abs(Physics2D.gravity.y);
        float gUp = (2f * Mathf.Max(0.0001f, apexHeight)) /
                    (Mathf.Max(0.0001f, timeToApex) * Mathf.Max(0.0001f, timeToApex));
        upGravityScale = gUp / gWorld;
    }

    public static float ComputeInitialSpeed(float apexHeight, float timeToApex) {
        float H = Mathf.Max(0.0001f, apexHeight);
        float T = Mathf.Max(0.0001f, timeToApex);
        float gUp = (2f * H) / (T * T);
        return gUp * T;
    }

    public void ResetFallProfile() {
        fallTimer = 0f;
        prevVy = 0f;
        inApexCushion = false;
        cushionTimer = 0f;
    }
}
