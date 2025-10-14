using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class simpleWalk : MonoBehaviour {
    public simpleControls controls;            // input
    [Min(0f)] public float moveSpeed = 6f;     // u/s
    public SpriteRenderer spriteToFlip;        // opcional

    Rigidbody2D rb;

    void Reset() {
        controls = GetComponent<simpleControls>();
        spriteToFlip = GetComponentInChildren<SpriteRenderer>();
    }

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        // Asegurate de NO congelar Position Y en Constraints.
    }

    void FixedUpdate() {
        if (controls == null) return;

        int dir = controls.MoveAxis(); // -1, 0, +1

        // Solo tocamos la X. La Y la maneja la gravedad/salto.
        Vector2 v = rb.linearVelocity;
        v.x = dir * moveSpeed;
        rb.linearVelocity = v;

        // Flip visual simple
        if (spriteToFlip != null && dir != 0) {
            var s = spriteToFlip.transform.localScale;
            s.x = Mathf.Abs(s.x) * (dir < 0 ? -1f : 1f);
            spriteToFlip.transform.localScale = s;
        }
    }
}
