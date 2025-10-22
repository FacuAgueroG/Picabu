using UnityEngine;
using System.Collections;

/// <summary>
/// Air-stall para enemigos cuando reciben golpes con Ctrl (Ctrl+S / Ctrl+D).
/// Limita a N stalls por "air sequence" del ATACANTE.
/// Pensado para Rigidbody2D Dynamic (enemigo en aire).
/// </summary>
[DisallowMultipleComponent]
public class simpleEnemyAirStall : MonoBehaviour {
    [Header("Stall (ms)")]
    [Tooltip("Tiempo de stall por hit con Ctrl (en milisegundos).")]
    public float stallMs = 90f;

    [Header("Límite por secuencia del atacante")]
    [Tooltip("Máximo de stalls aceptados por 'air sequence' del atacante (primeros 3 golpes).")]
    [Range(1, 5)] public int maxStallsPerAttackerSeq = 3;

    [Header("Depuración")]
    public bool enableDebug = false;

    Rigidbody2D rb;
    Coroutine stallRoutine;

    // Tracking por secuencia del ATACANTE
    int lastAttackerSeqId = -1;
    int usedThisSeq = 0;

    /// <summary>
    /// Expone si el enemigo está actualmente en stall (para helpers de gravedad).
    /// </summary>
    public bool IsStalling { get; private set; } = false;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// Intenta aplicar stall. Cuenta por secuencia aérea del ATACANTE (seqId).
    /// </summary>
    public bool TryApplyStall(Transform attacker, int attackerAirSeqId) {
        // Si cambia el id de secuencia del atacante, reseteamos contador.
        if (attackerAirSeqId != lastAttackerSeqId) {
            lastAttackerSeqId = attackerAirSeqId;
            usedThisSeq = 0;
        }

        if (usedThisSeq >= maxStallsPerAttackerSeq) {
            if (enableDebug) Debug.Log($"{name}: EnemyAirStall rechazado (límite por secuencia alcanzado).");
            return false;
        }

        // Requiere cuerpo dinámico para que tenga sentido (enemigo en aire).
        if (rb == null || !rb.simulated || rb.bodyType != RigidbodyType2D.Dynamic) {
            if (enableDebug) Debug.Log($"{name}: EnemyAirStall ignorado (Rigidbody no-dynamic).");
            return false;
        }

        usedThisSeq++;
        if (stallRoutine != null) StopCoroutine(stallRoutine);
        stallRoutine = StartCoroutine(Co_StallForMs(Mathf.Max(0f, stallMs)));
        if (enableDebug) Debug.Log($"{name}: EnemyAirStall aplicado. usados={usedThisSeq}/{maxStallsPerAttackerSeq} seq={attackerAirSeqId}");
        return true;
    }

    IEnumerator Co_StallForMs(float ms) {
        if (rb == null) yield break;

        IsStalling = true;

        // Guardar gravedad y forzar vy = 0
        float prevGravity = rb.gravityScale;
        var v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        // Congelar vertical
        rb.gravityScale = 0f;
        float tEnd = Time.realtimeSinceStartup + (ms / 1000f);

        while (Time.realtimeSinceStartup < tEnd) {
            var cur = rb.linearVelocity;
            if (cur.y != 0f) {
                cur.y = 0f;
                rb.linearVelocity = cur;
            }
            yield return new WaitForFixedUpdate();
        }

        // Restaurar
        rb.gravityScale = prevGravity;
        IsStalling = false;
        stallRoutine = null;
    }

    /// <summary>
    /// Cancela inmediatamente cualquier stall en curso (para no interferir con arrastre/launch).
    /// </summary>
    public void Cancel() {
        if (stallRoutine != null) {
            StopCoroutine(stallRoutine);
            stallRoutine = null;
        }
        IsStalling = false;
    }
}
