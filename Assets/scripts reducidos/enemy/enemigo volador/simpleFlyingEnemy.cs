using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class simpleFlyingEnemy : MonoBehaviour {
    public enum FlyState { Floating, Returning, StunnedGround, StunnedAir }

    [Header("Referencia (SpriteRenderer)")]
    public SpriteRenderer spriteRenderer;

    [Header("Posición inicial")]
    public Vector2 initialPosition;

    [Header("Regreso (solo esto usa MoveTowards)")]
    [Min(0f)] public float returnSpeed = 1.5f;
    [Min(0f)] public float arrivalThreshold = 0.05f;
    [Tooltip("Si está activo, los S/D también empujan aunque el enemigo esté regresando a su punto.")]
    public bool allowPushWhileReturning = true;

    [Header("Empuje por golpe simple (S/D)")]
    [Min(0f)] public float pushDistance = 0.8f;
    [Min(0.01f)] public float pushSpeed = 8f;
    [Min(0f)] public float waitAfterPushSeconds = 1.2f;
    [Range(1, 6)] public int maxPushesPerSequence = 3;
    [Min(0f)] public float pushCooldownAfterThird = 1.0f;

    [Header("Suelo (raycast)")]
    public LayerMask groundMask;
    public Transform groundRayOrigin;
    [Min(0.05f)] public float groundRayDistance = 8f;      // fallback
    [Min(0.01f)] public float stopBeforeGround = 0.06f;    // “skin” anti-incruste

    [Header("Finalizador: Arrastre al suelo (DDD Hold + S) – Física")]
    [Tooltip("Aceleración extra hacia abajo (m/s²) aplicada durante el arrastre (además de la gravedad).")]
    [Min(0f)] public float dragAccel = 90f;

    [Min(0.01f)] public float stunGroundSeconds = 1.25f; // también al caer tras launch

    [Header("Colores")]
    public Color normalColor = Color.clear;
    public Color stunColor = Color.white;

    [Header("Depuración (general)")]
    public bool enableDebug = false;

    [Header("Depuración de Arrastre (telemetría)")]
    public bool logDragTelemetry = false;
    [Min(1)] public int logFramesMax = 120;

    [Header("Runtime (solo lectura)")]
    public FlyState state = FlyState.Floating;
    public bool isReturning = false;
    public bool isAtInitial = true;

    // Tierra (compartidos)
    [Header("Tierra (componentes compartidos)")]
    public simpleEnemyGround2D groundCheck;
    public simpleEnemyState enemyState;
    public simpleLaunch sharedLaunch;

    // Gravedad integrada (rampa/cushion + topes contextuales)
    public simpleEnemyGravity2D gravityHelper;

    Rigidbody2D rb;
    Collider2D col;
    Coroutine _moveRoutine;
    Coroutine _waitN_Routine;
    Coroutine _cdRoutine;
    Coroutine _stunRoutine;   // SOLO para STUN EN SUELO

    int _pushCount = 0;
    bool _cooldownActive = false;
    bool _resumeReturnAfterPush = false;

    void Reset() {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (groundCheck == null) groundCheck = GetComponent<simpleEnemyGround2D>();
        if (enemyState == null) enemyState = GetComponent<simpleEnemyState>();
        if (sharedLaunch == null) sharedLaunch = GetComponent<simpleLaunch>();
        if (col == null) col = GetComponent<Collider2D>();
        if (gravityHelper == null) gravityHelper = GetComponent<simpleEnemyGravity2D>();
    }

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        initialPosition = transform.position;
        state = FlyState.Floating;
        isReturning = false;
        isAtInitial = true;

        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (normalColor == Color.clear && spriteRenderer != null) normalColor = spriteRenderer.color;
        ApplyNormalColor();

        if (groundCheck == null) groundCheck = GetComponent<simpleEnemyGround2D>();
        if (enemyState == null) enemyState = GetComponent<simpleEnemyState>();
        if (sharedLaunch == null) sharedLaunch = GetComponent<simpleLaunch>();
        if (gravityHelper == null) gravityHelper = GetComponent<simpleEnemyGravity2D>();

        if (groundCheck != null) {
            if (groundCheck.groundPoint == null && groundRayOrigin != null)
                groundCheck.groundPoint = groundRayOrigin;
            groundCheck.groundMask = groundMask;
        }

        // Arranca volando → kinematic
        SetAerialMode(); // Kinematic + simulated ON
    }

    void Update() {
        // Regreso (único lugar con MoveTowards)
        if (isReturning) {
            Vector2 cur = transform.position;
            Vector2 next = Vector2.MoveTowards(cur, initialPosition, returnSpeed * Time.deltaTime);
            transform.position = next;

            if (Vector2.Distance(next, initialPosition) <= arrivalThreshold) {
                isReturning = false;
                isAtInitial = true;
                OnReachedInitial();
                ResetPushSequence();
                if (state != FlyState.StunnedGround && state != FlyState.StunnedAir)
                    state = FlyState.Floating;
            }
            else {
                isAtInitial = false;
            }
        }

        // StunGround -> despega (launch) => StunnedAir hasta tocar suelo
        if (state == FlyState.StunnedGround && enemyState != null && enemyState.airState == EnemyAirState.InAir) {
            BeginAirStunOnLaunch();
        }

        // StunnedAir -> toca suelo => StunnedGround por R
        if (state == FlyState.StunnedAir && enemyState != null && enemyState.airState == EnemyAirState.OnGround) {
            if (_stunRoutine == null) {
                EnterGroundStun(stunGroundSeconds);
            }
        }
    }

    // ======== Señales desde el sensor ========

    public void NotifySimpleHit(AttackAreaKind areaKind, bool isCtrl) {
        // Bloquear mini-empujes si el volador está stuneado (aire o suelo)
        if (state == FlyState.StunnedAir || state == FlyState.StunnedGround) return;

        if (_cooldownActive) return;

        bool canPush = (_pushCount < maxPushesPerSequence);

        if (isReturning) {
            if (allowPushWhileReturning && canPush) {
                isReturning = false;
                _resumeReturnAfterPush = true;
            }
            else {
                return;
            }
        }
        else {
            if (!canPush) {
                BeginPushCooldown();
                return;
            }
            _resumeReturnAfterPush = false;
        }

        StopAllMotionCoroutines();
        _moveRoutine = StartCoroutine(Co_PushDownOnce(pushDistance, pushSpeed));

        _pushCount = Mathf.Clamp(_pushCount + 1, 0, maxPushesPerSequence);
        if (_pushCount >= maxPushesPerSequence) BeginPushCooldown();

        if (enableDebug) {
            Debug.Log($"{name}: SimpleHit area={areaKind} pushes={_pushCount}/{maxPushesPerSequence} cd={_cooldownActive} returning(before)={(isReturning ? "Y" : "N")} resumeAfterPush={_resumeReturnAfterPush}");
        }
    }

    public void ReceiveSpecialEffect(AttackAreaKind areaKind, AttackEffectKind effect, Transform attacker, int attackerAirSeqId) {
        if (effect == AttackEffectKind.DragToGround) {
            StopAllMotionCoroutines();
            StartCoroutine(Co_DragToGround_UsingPhysics());
            return;
        }
        // Launch lo maneja simpleLaunch; la transición a StunnedAir se detecta en Update().
    }

    // ======== Movimiento / estados ========

    IEnumerator Co_PushDownOnce(float distance, float speed) {
        Vector2 start = transform.position;
        Vector2 target = start + Vector2.down * Mathf.Max(0f, distance);

        UseKinematic(); // pequeño desplazamiento “arcade” controlado
        while (true) {
            Vector2 cur = transform.position;
            if (Vector2.Distance(cur, target) <= 0.001f) { transform.position = target; break; }
            Vector2 next = Vector2.MoveTowards(cur, target, speed * Time.deltaTime);
            transform.position = next;
            yield return null;
        }

        _moveRoutine = null;

        if (_resumeReturnAfterPush) {
            _resumeReturnAfterPush = false;
            StartReturn();
        }
        else {
            RestartWaitN();
        }
    }

    // === Arrastre con FÍSICA (empuje determinista + gravedad) ===
    IEnumerator Co_DragToGround_UsingPhysics() {
        // 1) Buscar suelo real (ray infinito)
        Vector2 origin = (groundRayOrigin != null) ? (Vector2)groundRayOrigin.position : (Vector2)transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, Mathf.Infinity, groundMask);

        float groundY;
        bool gotGround = hit.collider != null;
        if (gotGround) {
            groundY = hit.point.y;
        }
        else {
            // Fallback (no debería pasar si el mask está bien)
            groundY = transform.position.y - Mathf.Max(groundRayDistance, 0.5f);
            if (enableDebug) Debug.LogWarning($"{name}: DragToGround sin hit de suelo (mask {groundMask}). Fallback limitado.");
        }

        // 2) Objetivo por BOUNDS (no por pivot)
        float targetCenterY = ComputeTargetCenterYFromBounds(groundY, stopBeforeGround);

        // 3) Preparar física
        StopReturn();
        UseDynamicPhysicsForDrag();

        // Arrastre debe dominar: forzar caída pesada y usar tope alto en gravedad
        if (gravityHelper != null) gravityHelper.ForceMaxFallUntilGround();

        // 4) Loop de física (Fixed): empuje determinista + gravedad
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

            // Predicción simple para “snap” seguro al suelo
            float predictedY = rb.position.y + v.y * dt;
            if (predictedY <= targetCenterY) {
                Vector2 p = rb.position; p.y = targetCenterY; rb.position = p;
                rb.linearVelocity = Vector2.zero; // parar justo arriba del suelo
                break;
            }

            yield return waitFixed;
        }

        // 5) Stun de suelo
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
            return groundY + skin; // fallback
        }
    }

    void EnterGroundStun(float seconds) {
        SetGroundMode(); // lanzable (Dynamic)
        state = FlyState.StunnedGround;

        if (_stunRoutine != null) StopCoroutine(_stunRoutine);
        _stunRoutine = StartCoroutine(Co_GroundStun(seconds));
    }

    IEnumerator Co_GroundStun(float seconds) {
        ApplyStunColor();

        float tEnd = Time.time + Mathf.Max(0f, seconds);
        while (Time.time < tEnd) yield return null;

        _stunRoutine = null;

        ApplyNormalColor();
        SetAerialMode();
        state = FlyState.Floating;
        StartReturn();
    }

    void BeginAirStunOnLaunch() {
        StopReturn();
        if (_stunRoutine != null) { StopCoroutine(_stunRoutine); _stunRoutine = null; }
        UseDynamic(); // dejar la física del launch
        state = FlyState.StunnedAir;
        ApplyStunColor();

        if (gravityHelper != null) {
            gravityHelper.CancelForceMaxFallUntilGround(); // asegurar que no quede el modo drag
            gravityHelper.ResetFallProfile();
        }

        if (enableDebug) Debug.Log($"{name}: BeginAirStunOnLaunch -> StunnedAir (esperando suelo).");
    }

    // ======== util / timers ========

    void RestartWaitN() {
        if (_waitN_Routine != null) StopCoroutine(_waitN_Routine);
        _waitN_Routine = StartCoroutine(Co_WaitN_ThenReturn(waitAfterPushSeconds));
    }

    IEnumerator Co_WaitN_ThenReturn(float seconds) {
        float tEnd = Time.time + Mathf.Max(0f, seconds);
        while (Time.time < tEnd) yield return null;
        _waitN_Routine = null;
        StartReturn();
    }

    void BeginPushCooldown() {
        if (_cdRoutine != null) StopCoroutine(_cdRoutine);
        _cdRoutine = StartCoroutine(Co_PushCooldown(pushCooldownAfterThird));
    }

    IEnumerator Co_PushCooldown(float seconds) {
        _cooldownActive = true;
        float tEnd = Time.time + Mathf.Max(0f, seconds);
        while (Time.time < tEnd) yield return null;
        _cooldownActive = false;
        _cdRoutine = null;
    }

    void StopAllMotionCoroutines() {
        if (_moveRoutine != null) { StopCoroutine(_moveRoutine); _moveRoutine = null; }
        if (_waitN_Routine != null) { StopCoroutine(_waitN_Routine); _waitN_Routine = null; }
    }

    void ResetPushSequence() {
        _pushCount = 0;
        _cooldownActive = false;
        _resumeReturnAfterPush = false;
        if (_cdRoutine != null) { StopCoroutine(_cdRoutine); _cdRoutine = null; }
        if (_waitN_Routine != null) { StopCoroutine(_waitN_Routine); _waitN_Routine = null; }
    }

    // ======== Modo aéreo / tierra ========

    void SetAerialMode() {
        UseKinematic();
        if (enemyState != null) enemyState.kind = EnemyKind.Air;
    }

    void SetGroundMode() {
        UseDynamic();
        if (enemyState != null) enemyState.kind = EnemyKind.Ground;
    }

    void UseKinematic() {
        if (rb == null) return;
        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
    }

    void UseDynamic() {
        if (rb == null) return;
        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Dynamic;
    }

    void UseDynamicPhysicsForDrag() {
        if (rb == null) return;
        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.linearVelocity = Vector2.zero; // limpiar inercia previa
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    // ======== API general ========

    public void StartReturn() {
        isReturning = true;
        isAtInitial = false;
        if (_moveRoutine != null) { StopCoroutine(_moveRoutine); _moveRoutine = null; }
        if (state != FlyState.StunnedGround && state != FlyState.StunnedAir)
            state = FlyState.Returning;
    }

    public void StopReturn() { isReturning = false; }

    public void ApplyStunColor() { if (spriteRenderer != null) spriteRenderer.color = stunColor; }
    public void ApplyNormalColor() { if (spriteRenderer != null) spriteRenderer.color = normalColor; }

    protected virtual void OnReachedInitial() {
        if (enableDebug) Debug.Log($"{name}: Reached initialPosition {initialPosition}. Sequence reset.");
    }

    void OnDrawGizmosSelected() {
        Gizmos.color = Color.cyan;
        Vector3 gizPos = initialPosition;
        if (!Application.isPlaying && initialPosition == Vector2.zero) gizPos = transform.position;
        Gizmos.DrawWireSphere((Vector3)gizPos, 0.08f);
        Gizmos.DrawLine((Vector3)gizPos + Vector3.up * 0.12f, (Vector3)gizPos + Vector3.down * 0.12f);
        Gizmos.DrawLine((Vector3)gizPos + Vector3.right * 0.12f, (Vector3)gizPos + Vector3.left * 0.12f);

        Gizmos.color = Color.magenta;
        Vector3 rayFrom = (groundRayOrigin != null ? groundRayOrigin.position : transform.position);
        Gizmos.DrawLine(rayFrom, rayFrom + Vector3.down * groundRayDistance);
    }
}
