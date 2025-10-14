using UnityEngine;

public class simpleGround2D : MonoBehaviour {
    [Header("Ground Check")]
    public Transform groundPoint;     // Empty en los pies
    public float groundRadius = 0.08f;
    public LayerMask groundMask;

    public bool IsGrounded() {
        if (groundPoint == null) return false;
        return Physics2D.OverlapCircle(groundPoint.position, groundRadius, groundMask) != null;
    }

    void OnDrawGizmosSelected() {
        if (groundPoint == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundPoint.position, groundRadius);
    }
}
