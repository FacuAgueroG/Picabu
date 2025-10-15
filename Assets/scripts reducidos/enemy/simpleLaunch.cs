using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class simpleLaunch : MonoBehaviour {
    [Header("Parámetros del lanzamiento")]
    public float launchForce = 10f;  // cuán alto lo lanza
    Rigidbody2D rb;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
    }

    public void ReceiveLaunch() {
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(Vector2.up * launchForce, ForceMode2D.Impulse);
        // Debug.Log("Enemy " + name + " lanzado al aire con fuerza " + launchForce);
    }
}
