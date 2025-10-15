using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class simpleLaunch : MonoBehaviour {
    [Header("Parámetros del lanzamiento")]
    public float launchForce = 10f;  // cuán alto lo lanza

    [Header("Estado del enemigo (requerido para bloquear relanzos)")]
    public simpleEnemyState state;   // <- nuevo

    Rigidbody2D rb;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        if (state == null) state = GetComponent<simpleEnemyState>();
    }

    /// <summary>
    /// Intenta lanzar al enemigo. Si no se puede (ya está en aire, o es 'de aire'),
    /// simplemente no hace nada.
    /// </summary>
    public void ReceiveLaunch() {
        // Si hay estado y no es lanzable ahora, aborta.
        if (state != null && !state.CanBeLaunched()) return;

        // Aplicar lanzamiento
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(Vector2.up * launchForce, ForceMode2D.Impulse);

        // Marcar estado en aire para bloquear relanzamientos
        if (state != null) state.NotifyLaunched();
    }
}
