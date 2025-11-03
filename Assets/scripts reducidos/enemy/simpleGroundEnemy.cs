using UnityEngine;
using System.Collections;

/// <summary>
/// Espejo del volador para enemigos de SUELO:
/// - SOLO entra a StunnedAir si hubo un Launch real (armado por simpleLaunch).
/// - Al tocar suelo desde StunnedAir -> StunnedGround Xs.
/// - Si recibe DragToGround en el aire -> snap al suelo -> StunnedGround Xs.
/// - Si spawnea en el aire y cae, NO se stunea (porque no hubo launch armado).
/// - No hace return al salir del stun (queda donde está).
/// </summary>
[DisallowMultipleComponent]
public class simpleGroundEnemy : MonoBehaviour {
    public enum GroundState { Idle, StunnedGround, StunnedAir }

    [Header("Colores")]
    public Color normalColor = Color.white;
    public Color stunColor = Color.white;
    public SpriteRenderer spriteRenderer;

    [Header("Suelo (raycast para arrastre)")]
    public LayerMask groundMask;
    public Transform groundRayOrigin;
    [Min(0.05f)] public float groundRayDistance = 8f;
    [Min(0.01f)] public float stopBeforeGround = 0.06f;

    [Header("Arrastre (igual que volador)")]
    [Min(0f)] public float dragAccel = 90f;

    [Header("Stun en suelo tras aterrizar")]
    [Min(0.01f)] public float stunGroundSeconds = 1.25f;

    [Header("Refs compartidas")]
    public simpleEnemyState enemyState;              // OnGround/InAir
    public simpleEnemyGravity2D gravityHelper;      // rampa + cushion + topes contextuales
    public simpleLaunch sharedLaunch;               // launch aplicado por sensor
    public Collider2D col;

    [Header("Runtime (solo lectura)")]
    public GroundState state = GroundState.Idle;

    Rigidbody2D rb;
    Coroutine _stunRoutine;
    Coroutine _dragRoutine;

    EnemyAirState _prevAir;

    // Armador de stun post-launch
    bool _launchArmed = false;

    void Reset() {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (enemyState == null) enemyState = GetComponent<simpleEnemyState>();
        if (gravityHelper == null) gravityHelper = GetComponent<simpleEnemyGravity2D>();
        if (sharedLaunch == null) sharedLaunch = GetComponent<simpleLaunch>();
        if (col == null) col = GetComponent<Collider2D>();
    }

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (enemyState == null) enemyState = GetComponent<simpleEnemyState>();
        if (gravityHelper == null) gravityHelper = GetComponent<simpleEnemyGravity2D>();
        if (sharedLaunch == null) sharedLaunch = GetComponent<simpleLaunch>();
        if (col == null) col = GetComponent<Collider2D>();

        ApplyNormalColor();

        // Asegurar tipo suelo
        if (enemyState != null) enemyState.kind = EnemyKind.Ground;

        // Importante: tomar el estado AÉREO actual para no confundir el primer frame
        _prevAir = (enemyState != null) ? enemyState.airState : EnemyAirState.OnGround;
    }

    void Update() {
        if (enemyState == null) return;

        // Transición a aire: SOLO si venimos del suelo y hubo launch armado
        if (_prevAir == EnemyAirState.OnGround &&
            enemyState.airState == EnemyAirState.InAir &&
            _launchArmed) {
            BeginAirStunOnLaunch();
            _launchArmed = false; // se consume el armado
        }

        // StunnedAir -> toca suelo => StunnedGround
        if (state == GroundState.StunnedAir &&
            enemyState.airState == EnemyAirState.OnGround) {
            if (_stunRoutine == null) {
                EnterGroundStun(stunGroundSeconds);
            }
        }

        _prevAir = enemyState.airState;
    }

    // ===== API desde simpleLaunch =====
    public void ArmStunOnNextAirborneFromLaunch() {
        _launchArmed = true;
    }

    // ===== Efectos especiales (desde sensor) =====
    public void ReceiveSpecialEffect(AttackAreaKind areaKind, AttackEffectKind effect, Transform attacker, int attackerAirSeqId) {
        if (effect == AttackEffectKind.DragToGround) {
            // Solo tiene sentido si está en el aire (por launch o lo que sea)
            if (enemyState != null && enemyState.airState == EnemyAirState.InAir) {
                if (_dragRoutine != null) StopCoroutine(_dragRoutine);
                _dragRoutine = StartCoroutine(Co_DragToGround_UsingPhysics());
            }
        }
    }

    // ===== Estados =====
    void BeginAirStunOnLaunch() {
        // Cancelar arrastre activo si lo hubiera
        if (_dragRoutine != null) { StopCoroutine(_dragRoutine); _dragRoutine = null; }

        // Dejar la física del launch
        UseDynamic();

        state = GroundState.StunnedAir;
        ApplyStunColor();

        // Activar perfil de caída con rampa + cushion (igual que volador)
        if (gravityHelper != null) {
            gravityHelper.CancelForceMaxFallUntilGround(); // por si quedó latente de un arrastre previo
            gravityHelper.ResetFallProfile();
        }
    }

    void EnterGroundStun(float seconds) {
        state = GroundState.StunnedGround;
        if (_stunRoutine != null) StopCoroutine(_stunRoutine);
        _stunRoutine = StartCoroutine(Co_GroundStun(seconds));
    }

    IEnumerator Co_GroundStun(float seconds) {
        ApplyStunColor();

        float tEnd = Time.time + Mathf.Max(0f, seconds);
        while (Time.time < tEnd) yield return null;

        _stunRoutine = null;
        state = GroundState.Idle;
        ApplyNormalColor();
        // No hay return: queda donde está
    }

    // ===== Arrastre (idéntico a volador, sin return) =====
    IEnumerator Co_DragToGround_UsingPhysics() {
        // 1) Ray al suelo
        Vector2 origin = (groundRayOrigin != null) ? (Vector2)groundRayOrigin.position : (Vector2)transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, Mathf.Infinity, groundMask);

        float groundY;
        bool gotGround = hit.collider != null;
        if (gotGround) {
            groundY = hit.point.y;
        }
        else {
            groundY = transform.position.y - Mathf.Max(groundRayDistance, 0.5f);
            Debug.LogWarning($"{name}: DragToGround (ground) sin hit de suelo. Fallback limitado.");
        }

        // 2) Objetivo por bounds
        float targetCenterY = ComputeTargetCenterYFromBounds(groundY, stopBeforeGround);

        // 3) Preparar física: caída dominada por arrastre
        UseDynamicPhysicsForDrag();
        if (gravityHelper != null) gravityHelper.ForceMaxFallUntilGround(); // arrastre domina y usa tope alto

        // 4) Loop de empuje + snap
        var waitFixed = new WaitForFixedUpdate();
        while (true) {
            float dt = Time.fixedDeltaTime;

            Vector2 v = rb.linearVelocity;
            float vyAfter = v.y - dragAccel * dt;

            // Tope dinámico: usar el de gravedad (alto durante arrastre)
            float cap = (gravityHelper != null) ? gravityHelper.GetCurrentMaxFallSpeed() : 150f;
            if (vyAfter < -cap) vyAfter = -cap;

            v.y = vyAfter;
            rb.linearVelocity = v;

            float predictedY = rb.position.y + v.y * dt;
            if (predictedY <= targetCenterY) {
                Vector2 p = rb.position; p.y = targetCenterY; rb.position = p;
                rb.linearVelocity = Vector2.zero;
                break;
            }

            yield return waitFixed;
        }

        _dragRoutine = null;

        // 5) Impacto => Stun de suelo
        EnterGroundStun(stunGroundSeconds);
    }

    float ComputeTargetCenterYFromBounds(float groundY, float skin) {
        if (col != null) {
            var b = col.bounds;
            float extY = b.extents.y;
            float targetMinY = groundY + skin;
            return targetMinY + extY;
        }
        else {
            return groundY + skin;
        }
    }

    // ===== Util =====
    void ApplyStunColor() { if (spriteRenderer != null) spriteRenderer.color = stunColor; }
    void ApplyNormalColor() { if (spriteRenderer != null) spriteRenderer.color = normalColor; }

    void UseDynamic() {
        if (rb == null) return;
        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Dynamic;
    }

    void UseDynamicPhysicsForDrag() {
        if (rb == null) return;
        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.linearVelocity = Vector2.zero;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }
}
