using UnityEngine;

/// <summary>
/// Lanza al enemigo. Soporta dos modos:
/// 1) Legacy por fuerza (impulso/velocidad fija).
/// 2) Height+Time (apexHeight / timeToApex) para emparejar con el jump del player.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class simpleLaunch : MonoBehaviour {
    [Header("Estado del enemigo (bloquea relanzos en aire)")]
    public simpleEnemyState state;

    Rigidbody2D rb;

    [Header("Modo Legacy (simple)")]
    [Tooltip("Si Height+Time está DESACTIVADO, se usa este valor legacy.")]
    public float launchForce = 10f;  // impulso hacia arriba (legacy)

    [Header("Height + Time (como el Jump del player)")]
    public bool useHeightAndTime = true;

    [Tooltip("Altura al ápice (H). Igualá esto al del player para sincronizar alturas.")]
    public float apexHeight = 3.0f;

    [Tooltip("Tiempo al ápice (T). Igualá esto al del player para sincronizar tiempos.")]
    public float timeToApex = 0.35f;

    [Header("Gravedad del enemigo (recomendado)")]
    [Tooltip("Perfil de gravedad del enemigo. Ajusta upGravityScale al usar Height+Time.")]
    public simpleEnemyGravity2D gravityHelper;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        if (state == null) state = GetComponent<simpleEnemyState>();
        if (gravityHelper == null) gravityHelper = GetComponent<simpleEnemyGravity2D>();
    }

    /// <summary>
    /// Intenta lanzar al enemigo. Respeta el estado:
    /// - Solo lanza si es de tierra y está en suelo (o si state es null).
    /// - En Height+Time, configura upGravityScale y aplica la velocidad exacta.
    /// - En Legacy, aplica un impulso simple.
    /// </summary>
    public void ReceiveLaunch() {
        if (state != null && !state.CanBeLaunched()) return;

        if (useHeightAndTime) {
            // 1) Configurar gravedad de subida para que H/T coincidan
            if (gravityHelper != null) {
                gravityHelper.ConfigureUpGravityFrom(apexHeight, timeToApex);
                gravityHelper.ResetFallProfile();
            }

            // 2) Velocidad inicial exacta para alcanzar H en T (bajo gUp)
            float v0 = simpleEnemyGravity2D.ComputeInitialSpeed(apexHeight, timeToApex);

            // 3) Aplicar lanzamiento: anulamos vy y seteamos v0
            Vector2 v = rb.linearVelocity;
            v.y = v0;
            rb.linearVelocity = v;
        }
        else {
            // Modo legacy (compatibilidad): “fuerza”/impulso hacia arriba.
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
            rb.AddForce(Vector2.up * launchForce, ForceMode2D.Impulse);
        }

        // Notificar estado base
        if (state != null) state.NotifyLaunched();

        // === NUEVO ===
        // Armar el ciclo de stun solo en enemigos de suelo (no voladores)
        var groundEnemy = GetComponent<simpleGroundEnemy>();
        if (groundEnemy != null) {
            groundEnemy.ArmStunOnNextAirborneFromLaunch();
        }
    }
}
