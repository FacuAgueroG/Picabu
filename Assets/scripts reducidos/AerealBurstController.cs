using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aerial Burst (SSS Hold>=3 + D en AIRE) – PARTE 2 COMPLETA
/// Corrección:
///  - Reutiliza AerialBurstVictimDriver si ya existe (NO hace Destroy+Add en el mismo frame).
///  - Limpia SIEMPRE al terminar (drivers se destruyen y el player recupera control).
///  - Tolerante a nulos (no crashea si un collider no tiene RB).
/// </summary>
[DisallowMultipleComponent]
public class AerialBurstController : MonoBehaviour {
    [Header("Referencias (Player)")]
    public Transform playerCenter;
    public Rigidbody2D playerRb;
    public simpleControls controlsToLock;
    public simpleAirStall playerAirStall; // opcional

    [Header("Visual del área (opcional)")]
    public SpriteRenderer areaSprite;
    public float detectRadius = 3.8f;
    public LayerMask enemyMask;

    [Header("Etapa 1 (distancias/tiempos)")]
    public float stage1AirPushX = 2.0f;   // Aéreos: empuje horizontal
    public float stage1Time = 0.20f;      // También usado para elevar a suelo
    // Para suelo en etapa 1 usamos la MISMA distancia (elevación vertical = stage1AirPushX)

    [Header("Apagado y espera entre etapas")]
    public float offBetweenStages = 0.12f;

    [Header("Etapa 2 (distancias/tiempos)")]
    public float stage2AirPushX_R = 4.0f; // Aéreos: empuje horizontal extra
    public float stage2GroundPushG = 2.25f; // Suelo: X lejos del player mientras vuelve a Y0
    public float stage2Time_D = 0.25f;

    [Header("Suspensión del player")]
    [Tooltip("Si >0, aplica AirStall al player durante cada tramo; si =0, congela gravedad manualmente.")]
    public float stallMsPerActiveChunk = 0f;

    // runtime
    bool _running = false;
    readonly List<AerialBurstVictimDriver> _victims = new List<AerialBurstVictimDriver>();

    void Reset() {
        if (playerCenter == null) playerCenter = transform;
        if (playerRb == null) playerRb = GetComponent<Rigidbody2D>();
        if (controlsToLock == null) controlsToLock = GetComponent<simpleControls>();
        if (playerAirStall == null) playerAirStall = GetComponent<simpleAirStall>();
    }

    // === API ===
    public void FireTwoStage() {
        if (!_running) StartCoroutine(Co_Run(part2: true));
    }

    // (Se deja por si querés testear solo etapa 1)
    public void FirePart1() {
        if (!_running) StartCoroutine(Co_Run(part2: false));
    }

    IEnumerator Co_Run(bool part2) {
        _running = true;
        _victims.Clear();

        // 1) Bloquear input + suspender al player
        bool weLockedInput = false;
        float prevG = 1f;

        try {
            if (controlsToLock != null && !controlsToLock.blockInput) {
                controlsToLock.blockInput = true;
                weLockedInput = true;
            }

            if (playerRb == null) playerRb = GetComponent<Rigidbody2D>();
            if (playerRb != null) {
                prevG = playerRb.gravityScale;
                playerRb.gravityScale = 0f;
                var v = playerRb.linearVelocity; v.y = 0f; playerRb.linearVelocity = v;
            }

            if (stallMsPerActiveChunk > 0f && playerAirStall != null)
                playerAirStall.ApplyStall(stallMsPerActiveChunk);

            // 2) Encender sprite y CAPTURAR (sin duplicados)
            SetAreaSprite(true);
            CaptureVictimsIntoList();

            // 3) Etapa 1 (todos en paralelo)
            yield return Co_Stage1_All();

            // 4) Apagar sprite y esperar
            SetAreaSprite(false);

            if (!part2) {
                // Parte 1: soltar control y terminar
                yield return new WaitForSeconds(offBetweenStages);
                yield break;
            }

            // Mantener suspendido entre etapas (opcional stall “suave”)
            if (stallMsPerActiveChunk > 0f && playerAirStall != null)
                playerAirStall.ApplyStall(stallMsPerActiveChunk);

            yield return new WaitForSeconds(offBetweenStages);

            // 5) Re-encender sprite (NO se capturan nuevos) y ejecutar etapa 2
            SetAreaSprite(true);
            yield return Co_Stage2_All();

            // 6) Apagar sprite
            SetAreaSprite(false);
        }
        finally {
            // 7) Limpieza SIEMPRE (aunque haya error): destruir drivers y restaurar player
            for (int i = 0; i < _victims.Count; i++) {
                var v = _victims[i];
                if (v != null) v.FinishAndDestroy();
            }
            _victims.Clear();

            if (playerRb != null) playerRb.gravityScale = prevG;
            if (controlsToLock != null && weLockedInput) controlsToLock.blockInput = false;

            _running = false;
        }
    }

    void SetAreaSprite(bool on) {
        if (areaSprite != null) areaSprite.enabled = on;
    }

    void CaptureVictimsIntoList() {
        Vector2 c = (playerCenter != null) ? (Vector2)playerCenter.position : (Vector2)transform.position;
        var cols = Physics2D.OverlapCircleAll(c, detectRadius, enemyMask);

        // Evitar duplicados por el mismo root
        var seen = new HashSet<Transform>();

        for (int i = 0; i < cols.Length; i++) {
            var col = cols[i];
            if (col == null) continue;

            // Tomar el root “propietario”
            Transform root = col.attachedRigidbody ? col.attachedRigidbody.transform : col.transform;
            if (root == null) root = col.transform;
            if (seen.Contains(root)) continue;
            seen.Add(root);

            // Buscar/crear driver SIN destruir en este frame
            if (!root.TryGetComponent<AerialBurstVictimDriver>(out var drv) || drv == null) {
                drv = root.gameObject.AddComponent<AerialBurstVictimDriver>();
            }

            // (Re)configurar en caliente de forma segura
            drv.ResetForReuse(playerCenter);
            _victims.Add(drv);
        }
    }

    IEnumerator Co_Stage1_All() {
        int running = 0;
        for (int i = 0; i < _victims.Count; i++) {
            var v = _victims[i];
            if (v == null) continue;
            running++;
            v.Stage1(
                pushAirX: stage1AirPushX,
                liftGroundY: stage1AirPushX,   // misma P1
                timeSec: stage1Time,
                onDone: () => running--
            );
        }

        if (stallMsPerActiveChunk > 0f && playerAirStall != null)
            playerAirStall.ApplyStall(Mathf.Ceil(stage1Time * 1000f));

        while (running > 0) yield return null;
    }

    IEnumerator Co_Stage2_All() {
        int running = 0;
        for (int i = 0; i < _victims.Count; i++) {
            var v = _victims[i];
            if (v == null) continue;
            running++;
            v.Stage2(
                pushAirX_R: stage2AirPushX_R,
                pushGroundG: stage2GroundPushG,
                timeSec: stage2Time_D,
                onDone: () => running--
            );
        }

        if (stallMsPerActiveChunk > 0f && playerAirStall != null)
            playerAirStall.ApplyStall(Mathf.Ceil(stage2Time_D * 1000f));

        while (running > 0) yield return null;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {
        if (playerCenter == null) playerCenter = transform;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(playerCenter.position, detectRadius);
    }
#endif
}
