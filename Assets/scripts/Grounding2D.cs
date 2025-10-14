using UnityEngine;

[DisallowMultipleComponent]
public class Grounding2D : MonoBehaviour {
    [Header("Ground Check (dual ray)")]
    public Vector2 groundRayOffsetLeft = new Vector2(-0.4f, -0.5f);
    public Vector2 groundRayOffsetRight = new Vector2(0.4f, -0.5f);
    public float groundRayLength = 0.2f;
    public LayerMask groundMask;
    public string groundTag = "Ground";

    [Header("One-Ways")]
    public bool allowDropThrough = true;
    public LayerMask oneWayMask;
    public string oneWayTag = "OneWay";
    public bool treatOneWayAsGround = true;
    public float dropThroughDuration = 0.25f;

    [Header("Debug")]
    public bool drawGizmos = true;

    Rigidbody2D rb;
    Collider2D col;
    PlayerBrain brain;

    public bool IsGrounded { get; private set; }
    public bool HasHorizontalInput => HorizontalIntent != 0;
    public int HorizontalIntent { get; private set; }
    public Collider2D LastGroundCollider { get; private set; }

    // Drop-through tracking
    public bool DropThroughActive { get; private set; }
    public Collider2D DropThroughCollider { get; private set; }
    float dropThroughTimer = 0f;
    public bool DropBlockWallGrabActive { get; private set; }

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        brain = GetComponent<PlayerBrain>();
    }

    public void TickSense() {
        HorizontalIntent = (brain ? brain.moveX : 0);

        // Update drop-through timers
        if (DropThroughActive) {
            dropThroughTimer -= Time.deltaTime;
            if (dropThroughTimer <= 0f) {
                DropThroughActive = false;
                DropThroughCollider = null;
            }
        }

        // Ground check (two rays)
        IsGrounded = CheckGroundRayAt(groundRayOffsetLeft) || CheckGroundRayAt(groundRayOffsetRight);
    }

    bool CheckGroundRayAt(Vector2 localOffset) {
        Vector2 origin = (Vector2)transform.position + localOffset;

        // 1) with mask
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundRayLength, groundMask);
        if (hit.collider != null) {
            if (!(DropThroughActive && SameCollider(hit.collider, DropThroughCollider))) {
                LastGroundCollider = hit.collider;
                return true;
            }
        }

        // 2) no mask fallback: tag ground or one-way considered ground
        RaycastHit2D hitNoMask = Physics2D.Raycast(origin, Vector2.down, groundRayLength);
        if (hitNoMask.collider != null) {
            var c = hitNoMask.collider;

            if (DropThroughActive && SameCollider(c, DropThroughCollider))
                return false;

            if (c.CompareTag(groundTag)) { LastGroundCollider = c; return true; }
            if (treatOneWayAsGround && IsOneWay(c)) { LastGroundCollider = c; return true; }
        }
        return false;
    }

    public bool IsOneWay(Collider2D c) {
        if (!c) return false;
        if ((oneWayMask.value & (1 << c.gameObject.layer)) != 0) return true;
        if (!string.IsNullOrEmpty(oneWayTag) && c.CompareTag(oneWayTag)) return true;
        if (c.GetComponent<PlatformEffector2D>() != null) return true;
        if (c.GetComponentInParent<PlatformEffector2D>() != null) return true;
        return false;
    }

    static bool SameCollider(Collider2D a, Collider2D b) => a && b && a == b;

    public bool TryDropThroughNow() {
        if (!allowDropThrough || !IsGrounded) return false;
        if (!LastGroundCollider || !IsOneWay(LastGroundCollider)) return false;

        StartCoroutine(DropThroughRoutine(LastGroundCollider, dropThroughDuration));
        return true;
    }

    System.Collections.IEnumerator DropThroughRoutine(Collider2D platform, float duration) {
        if (!platform) yield break;

        DropThroughActive = true;
        DropThroughCollider = platform;
        dropThroughTimer = duration;

        DropBlockWallGrabActive = true;
        Physics2D.IgnoreCollision(col, platform, true);

        IsGrounded = false;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Min(rb.linearVelocity.y, -0.1f));

        float t = 0f;
        while (t < duration) { t += Time.deltaTime; yield return null; }

        Physics2D.IgnoreCollision(col, platform, false);

        DropBlockWallGrabActive = false;

        // small grace so ground rays don�t re-hit immediately
        dropThroughTimer = 0.05f;
        DropThroughActive = true;
        DropThroughCollider = platform;
    }

    void OnDrawGizmosSelected() {
        if (!drawGizmos) return;
        Gizmos.color = Color.green;
        Vector2 originL = (Vector2)transform.position + groundRayOffsetLeft;
        Vector2 originR = (Vector2)transform.position + groundRayOffsetRight;
        Gizmos.DrawLine(originL, originL + Vector2.down * groundRayLength);
        Gizmos.DrawLine(originR, originR + Vector2.down * groundRayLength);
    }
}
