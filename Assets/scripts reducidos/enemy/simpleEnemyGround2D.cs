using UnityEngine;

public class simpleEnemyGround2D : MonoBehaviour {
    [Header("Ground Check (enemigos)")]
    [Tooltip("Empty en los 'pies' del enemigo.")]
    public Transform groundPoint;

    [Min(0f)]
    public float groundRadius = 0.08f;

    [Tooltip("Layers que cuentan como suelo para ENEMIGOS.")]
    public LayerMask groundMask;

    /// <summary>
    /// Devuelve true si el groundPoint está tocando suelo.
    /// </summary>
    public bool IsGrounded() {
        if (groundPoint == null) return false;
        return Physics2D.OverlapCircle(groundPoint.position, groundRadius, groundMask) != null;
    }

    void OnDrawGizmosSelected() {
        if (groundPoint == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundPoint.position, groundRadius);
    }
}
