using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controla el air-stall DEL ENEMIGO y decide si aplicarlo cuando recibe un hit.
/// - Aplica vy=0 y gravityScale=0 durante ms.
/// - Lleva el conteo de "solo en los N primeros Ctrl+S" POR atacante y POR secuencia aérea del atacante.
/// - Si se aplica al menos un stall por Ctrl+S, le indica a la gravedad que fuerce caída a Max hasta tocar suelo.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class simpleEnemyAirStall : MonoBehaviour {
    [Header("Duración por defecto")]
    [Min(0f)] public float defaultStallMs = 90f;

    [Header("Límite por atacante/secuencia")]
    [Tooltip("Máximo de stalls que este enemigo acepta por cada atacante en una misma 'air sequence' del atacante (Ctrl+S).")]
    public int maxStallsPerAttackerAirSeq = 3;

    Rigidbody2D rb;
    Coroutine stallRoutine;
    public bool IsStalling { get; private set; }

    // Ref a la gravedad para forzar modo "Max hasta suelo" tras un stall aceptado
    simpleEnemyGravity2D gravityHelper;

    class PerAttackerTrack {
        public int lastAirSeqId = -1;
        public int countInSeq = 0;
    }
    readonly Dictionary<Transform, PerAttackerTrack> perAttacker = new Dictionary<Transform, PerAttackerTrack>();

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        gravityHelper = GetComponent<simpleEnemyGravity2D>();
    }

    /// <summary>
    /// Decide si este enemigo debe stallearse ante un hit.
    /// - Aplica stall SOLO si isCtrlS == true.
    /// - Respeta maxStallsPerAttackerAirSeq por atacante y por airSeqId del atacante.
    /// - Si se aplica, fuerza en la gravedad el modo "downGravityScaleMax hasta tocar suelo".
    /// </summary>
    public void ApplyStallFromHit(Transform attacker, int attackerAirSeqId, bool isCtrlS, float msSuggested = -1f) {
        if (!isCtrlS) return; // solo responde a Ctrl+S
        int seqId = Mathf.Max(0, attackerAirSeqId);

        if (attacker == null) {
            // Aceptamos (sin tracking) y forzamos modo Max-fall
            ApplyStall(msSuggested);
            if (gravityHelper != null) gravityHelper.ForceMaxFallUntilGround();
            return;
        }

        if (!perAttacker.TryGetValue(attacker, out var trk)) {
            trk = new PerAttackerTrack { lastAirSeqId = seqId, countInSeq = 0 };
            perAttacker[attacker] = trk;
        }

        if (trk.lastAirSeqId != seqId) {
            trk.lastAirSeqId = seqId;
            trk.countInSeq = 0;
        }

        if (trk.countInSeq >= Mathf.Max(0, maxStallsPerAttackerAirSeq)) return;

        // Aceptamos este stall
        trk.countInSeq++;
        ApplyStall(msSuggested);

        // 🚩 Indicar a la gravedad que, al salir del stall, use Max hasta suelo
        if (gravityHelper != null) gravityHelper.ForceMaxFallUntilGround();
    }

    /// <summary>Aplica el stall físico: vy=0 y gravedad anulada por ms.</summary>
    public void ApplyStall(float ms = -1f) {
        float dur = (ms > 0f) ? ms : defaultStallMs;
        if (stallRoutine != null) StopCoroutine(stallRoutine);
        stallRoutine = StartCoroutine(Co_Stall(dur));
    }

    IEnumerator Co_Stall(float ms) {
        IsStalling = true;

        // Zero out vertical inmediatamente
        Vector2 v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        float tEnd = Time.realtimeSinceStartup + (ms / 1000f);
        while (Time.realtimeSinceStartup < tEnd) {
            Vector2 cur = rb.linearVelocity;
            if (cur.y != 0f) {
                cur.y = 0f;
                rb.linearVelocity = cur;
            }
            yield return new WaitForFixedUpdate();
        }

        IsStalling = false;
        stallRoutine = null;
    }
}
