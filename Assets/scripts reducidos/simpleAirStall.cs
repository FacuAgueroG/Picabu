using System.Collections;
using UnityEngine;

/// <summary>
/// Congela SOLO el eje Y por un lapso (ms), sin tocar la X.
/// Diseñado para convivir con simpleGravity2D (que consultará IsStalling).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class simpleAirStall : MonoBehaviour {
    [Min(0f)] public float defaultStallMs = 110f;

    Rigidbody2D rb;
    Coroutine stallRoutine;
    public bool IsStalling { get; private set; } = false;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// Aplica un stall vertical durante ms (si ms<=0 usa default).
    /// No acumula: reinicia si hay uno corriendo.
    /// </summary>
    public void ApplyStall(float ms = -1f) {
        float dur = (ms > 0f) ? ms : defaultStallMs;
        if (stallRoutine != null) StopCoroutine(stallRoutine);
        stallRoutine = StartCoroutine(Co_Stall(dur));
    }

    IEnumerator Co_Stall(float ms) {
        IsStalling = true;

        // Forzar vy=0 de entrada
        Vector2 v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        float tEnd = Time.realtimeSinceStartup + (ms / 1000f);
        while (Time.realtimeSinceStartup < tEnd) {
            // Mantener vy=0 cada Fixed
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
