using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class DashModule2D : MonoBehaviour {
    [Header("Keys/Rules")]
    public bool allowDownwardDash = true;
    public bool slamOnDownwardDash = true;
    public float dashWallSafeDistance = 0.05f;

    [Header("Dash Kinematics")]
    public float dashSpeed = 20f;
    public float dashDistance = 6f;
    public float downwardDashSpeedDivisor = 2f;
    public float downwardGroundStopSkin = 0.025f;
    public float downwardCancelJumpForce = 9f;

    [Header("Charges/Buffer")]
    public int dashMaxCharges = 2;
    public float dashCooldown = 1.25f;
    public float dashBufferTime = 0.12f;

    [Header("Upward dash & One-Ways")]
    public LayerMask oneWayMask;

    Rigidbody2D rb;
    Collider2D col;
    PlayerBrain brain;
    Grounding2D grounding;
    WallModule2D wall;
    JumpModule2D jump;

    public bool IsDashing { get; private set; } = false;
    public bool IsDownwardHoldDash { get; private set; } = false;
    Vector2 dashDir = Vector2.zero;

    float dashBufferTimer = 0f;
    float[] cooldownLeft; bool[] ready; bool[] awaitGround;

    readonly List<Collider2D> tempIgnoredOneWays = new List<Collider2D>();

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        brain = GetComponent<PlayerBrain>();
        grounding = GetComponent<Grounding2D>();
        wall = GetComponent<WallModule2D>();
        jump = GetComponent<JumpModule2D>();

        int n = Mathf.Max(1, dashMaxCharges);
        cooldownLeft = new float[n];
        ready = new bool[n];
        awaitGround = new bool[n];
        for (int i = 0; i < n; i++) { cooldownLeft[i] = 0f; ready[i] = true; awaitGround[i] = false; }
    }

    void Update() {
        // charges
        bool groundLike = grounding.IsGrounded || (wall && wall.IsOnWall && wall.wallCountsAsGroundForDash);
        UpdateDashCharges(Time.deltaTime, groundLike);

        // dash buffer attempt
        if (dashBufferTimer > 0f && !IsDashing) {
            dashBufferTimer -= Time.deltaTime;
            if (dashBufferTimer > 0f && !jump.ExecutedJumpThisFrame) TryDashNow();
        }
    }

    public void HandleDashInput(bool dashDown) {
        if (IsDashing) return;

        if (dashDown) {
            bool fired = TryDashNow();
            if (!fired) dashBufferTimer = dashBufferTime;
        }
    }

    bool TryDashNow() {
        if (IsDashing) return false;

        int idx = GetReadyDashIndex();
        if (idx < 0) return false;

        Vector2 dir;
        if (grounding.IsGrounded) {
            // horizontal only on ground
            if (brain.holdUp || brain.holdDown) return false;
            int hx = (brain.holdLeft ^ brain.holdRight) ? (brain.holdLeft ? -1 : 1) : (brain.facingRight ? 1 : -1);
            dir = new Vector2(hx, 0f);
        }
        else {
            int x = brain.holdRight ? 1 : (brain.holdLeft ? -1 : 0);
            int y = brain.holdUp ? 1 : (brain.holdDown ? -1 : 0);
            dir = (x == 0 && y == 0) ? (brain.facingRight ? Vector2.right : Vector2.left) : new Vector2(x, y).normalized;

            if (!allowDownwardDash && dir.y < 0f) return false;
        }

        if (slamOnDownwardDash && dir.y < 0f) {
            ConsumeCharge(idx);
            StartCoroutine(DownwardHoldDashRoutine(dir.normalized));
            return true;
        }

        float allowed = ComputeDashAllowedDistance(dir);
        if (allowed <= 0.0001f) return false;

        if (dir.y > 0f) PrepareUpwardDashIgnoreOneWays(dir, allowed);

        ConsumeCharge(idx);
        StartCoroutine(DashRoutine(dir, allowed));
        return true;
    }

    float ComputeDashAllowedDistance(Vector2 dir) {
        if (dir.sqrMagnitude <= 0f) return 0f;

        var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true };
        filter.SetLayerMask(grounding.groundMask);

        float maxCheck = Mathf.Max(0f, dashDistance + dashWallSafeDistance);
        RaycastHit2D[] hits = new RaycastHit2D[16];
        int count = col.Cast(dir, filter, hits, maxCheck);

        float minHitDist = maxCheck;
        for (int i = 0; i < count; i++) {
            var h = hits[i];
            if (!h.collider) continue;
            if (dir.y > 0f && grounding.IsOneWay(h.collider)) continue; // ignore one-way when dashing up
            if (h.distance < minHitDist) minHitDist = h.distance;
        }
        return (minHitDist < maxCheck) ? Mathf.Clamp(minHitDist - dashWallSafeDistance, 0f, dashDistance) : dashDistance;
    }

    System.Collections.IEnumerator DownwardHoldDashRoutine(Vector2 dir) {
        IsDashing = true; IsDownwardHoldDash = true;
        jump.SetFallContext(JumpModule2D.FallContext.FromDash);
        rb.gravityScale = 0f;

        float speed = dashSpeed / Mathf.Max(0.01f, downwardDashSpeedDivisor);
        bool cancelQueued = false;

        var wait = new WaitForFixedUpdate();
        while (true) {
            // cancel with Jump
            if (Input.GetKeyDown(brain.jumpKey)) cancelQueued = true;

            if (cancelQueued) {
                IsDownwardHoldDash = false; IsDashing = false;
                rb.gravityScale = Mathf.Max(0.01f, rb.gravityScale);
                if (!grounding.IsGrounded && jump.CanDoubleJump) { // optional double
                    // spend double by normal double jump
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                    jump.SetFallContext(JumpModule2D.FallContext.FromJump);
                    jump.CanDoubleJump = false;
                    rb.AddForce(Vector2.up * jump.jumpMinForce, ForceMode2D.Impulse);
                }
                else {
                    jump.DoDownwardCancelJump(downwardCancelJumpForce);
                }
                yield break;
            }

            float dt = Time.fixedDeltaTime;
            float step = speed * dt;

            // front cast to stop before ground
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true };
            filter.SetLayerMask(grounding.groundMask);

            RaycastHit2D[] hits = new RaycastHit2D[8];
            int count = col.Cast(dir, filter, hits, step + grounding.groundRayLength);
            bool willHit = false; float hitDist = Mathf.Infinity;

            for (int i = 0; i < count; i++) {
                var h = hits[i];
                if (!h.collider) continue;
                hitDist = Mathf.Min(hitDist, h.distance);
                willHit = true;
            }

            if (willHit) {
                float moveDist = Mathf.Max(0f, hitDist - downwardGroundStopSkin);
                Vector2 nextPos = rb.position + dir * moveDist;
                rb.MovePosition(nextPos);

                // end dash
                rb.gravityScale = Mathf.Max(0.01f, rb.gravityScale);
                IsDownwardHoldDash = false; IsDashing = false;

                jump.SetFallContext(JumpModule2D.FallContext.FromDash);
                jump.TryConsumeDashJumpBuffer(); // optional buffered jump
                yield break;
            }
            else {
                rb.MovePosition(rb.position + dir * step);
            }

            yield return wait;
        }
    }

    System.Collections.IEnumerator DashRoutine(Vector2 dir, float allowedDistance) {
        IsDashing = true;
        jump.SetFallContext(JumpModule2D.FallContext.FromDash);

        float originalG = rb.gravityScale;
        rb.gravityScale = 0f;

        Vector2 targetPos = rb.position + dir * allowedDistance;
        var wait = new WaitForFixedUpdate();

        while ((rb.position - targetPos).sqrMagnitude > 0.0001f) {
            float step = dashSpeed * Time.fixedDeltaTime;
            Vector2 nextPos = Vector2.MoveTowards(rb.position, targetPos, step);
            rb.MovePosition(nextPos);
            yield return wait;
        }

        rb.gravityScale = originalG;
        RestoreIgnoredOneWays();

        IsDashing = false;

        // consume buffered jump if present
        jump.TryConsumeDashJumpBuffer();
    }

    void PrepareUpwardDashIgnoreOneWays(Vector2 dir, float distance) {
        tempIgnoredOneWays.Clear();
        if (oneWayMask.value == 0) return;

        var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true };
        filter.SetLayerMask(oneWayMask);

        RaycastHit2D[] hits = new RaycastHit2D[16];
        int count = col.Cast(dir, filter, hits, distance);
        for (int i = 0; i < count; i++) {
            var c = hits[i].collider;
            if (!c) continue;
            if (!grounding.IsOneWay(c)) continue;

            if (!tempIgnoredOneWays.Contains(c)) {
                Physics2D.IgnoreCollision(col, c, true);
                tempIgnoredOneWays.Add(c);
            }
        }
    }

    void RestoreIgnoredOneWays() {
        for (int i = 0; i < tempIgnoredOneWays.Count; i++) {
            var c = tempIgnoredOneWays[i];
            if (c) Physics2D.IgnoreCollision(col, c, false);
        }
        tempIgnoredOneWays.Clear();
    }

    void UpdateDashCharges(float dt, bool groundLike) {
        int n = cooldownLeft.Length;
        for (int i = 0; i < n; i++) {
            if (ready[i]) continue;

            if (cooldownLeft[i] > 0f) {
                cooldownLeft[i] -= dt;
                if (cooldownLeft[i] <= 0f) {
                    cooldownLeft[i] = 0f;
                    if (groundLike) { ready[i] = true; awaitGround[i] = false; }
                    else awaitGround[i] = true;
                }
            }
            else if (awaitGround[i] && groundLike) {
                ready[i] = true; awaitGround[i] = false;
            }
        }
    }

    int GetReadyDashIndex() { for (int i = 0; i < ready.Length; i++) if (ready[i]) return i; return -1; }
    void ConsumeCharge(int idx) {
        if (idx < 0 || idx >= ready.Length) return;
        ready[idx] = false; awaitGround[idx] = false; cooldownLeft[idx] = dashCooldown;
    }
}
