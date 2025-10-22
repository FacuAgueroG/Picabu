using UnityEngine;
using System.Collections.Generic;

public enum AttackAreaKind { S, D }   // distinguir área del golpe

public class simpleAttackSensor : MonoBehaviour {
    [Header("Collider del hitbox (isTrigger = true)")]
    public Collider2D targetCollider;

    [Header("Filtro de enemigos")]
    public LayerMask enemyLayers;
    public string enemyTag = "enemy";

    [Header("Área (S o D)")]
    public AttackAreaKind areaKind = AttackAreaKind.S;

    [Header("Metadatos (seteados por el atacante antes de BeginWindow)")]
    [HideInInspector] public Transform metaAttacker;
    [HideInInspector] public bool metaIsCtrl = false;      // Ctrl+S / Ctrl+D
    [HideInInspector] public int metaAttackerAirSeqId = 0; // id de secuencia aérea del atacante
    [HideInInspector] public bool metaIsHoldTick = false;  // true solo durante ticks de HOLD

    // Estado ventana
    bool windowOpen = false;
    AttackEffectKind currentEffect = AttackEffectKind.None;   // definido en simpleAttack.cs
    readonly HashSet<Transform> hitThisWindow = new HashSet<Transform>();
    static readonly Collider2D[] _overlapBuffer = new Collider2D[32];

    // ===== Fallback de secuencia aérea =====
    static readonly Dictionary<Transform, int> s_lastSeqId = new Dictionary<Transform, int>();
    static readonly Dictionary<Transform, bool> s_prevGrounded = new Dictionary<Transform, bool>();

    void Awake() {
        if (targetCollider == null)
            targetCollider = GetComponentInChildren<Collider2D>(true);
        if (targetCollider != null) targetCollider.isTrigger = true;
    }

    public void BeginWindow(AttackEffectKind effectKind) {
        currentEffect = effectKind;
        hitThisWindow.Clear();
        windowOpen = true;
        ScanOnce();
    }

    public void EndWindow() {
        windowOpen = false;
        hitThisWindow.Clear();
        currentEffect = AttackEffectKind.None;
    }

    void FixedUpdate() {
        if (!windowOpen) return;
        ScanOnce();
    }

    void ScanOnce() {
        if (targetCollider == null || !targetCollider.enabled) return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        if (enemyLayers.value != 0) { filter.SetLayerMask(enemyLayers); filter.useLayerMask = true; }
        else { filter.useLayerMask = false; }

        int count = targetCollider.Overlap(filter, _overlapBuffer);
        for (int i = 0; i < count && i < _overlapBuffer.Length; i++) {
            var other = _overlapBuffer[i];
            _overlapBuffer[i] = null;
            TryHit(other);
        }
    }

    bool IsEnemy(GameObject go) {
        bool byLayer = (enemyLayers.value & (1 << go.layer)) != 0;
        bool byTag = (!string.IsNullOrEmpty(enemyTag) && go.CompareTag(enemyTag));
        return byLayer || byTag;
    }

    // ===== Helpers de fallback =====
    int GetOrComputeAirSeqId(Transform attacker) {
        if (attacker == null) return 0;

        if (metaAttackerAirSeqId > 0) return metaAttackerAirSeqId;

        var ground = attacker.GetComponent<simpleGround2D>();
        bool groundedNow = (ground != null) && ground.IsGrounded();

        bool prevG;
        if (!s_prevGrounded.TryGetValue(attacker, out prevG)) {
            s_prevGrounded[attacker] = groundedNow;
            if (!groundedNow) {
                s_lastSeqId[attacker] = (s_lastSeqId.TryGetValue(attacker, out var cur) ? cur : 0) + 1;
            }
        }
        else {
            if (prevG && !groundedNow) {
                s_lastSeqId[attacker] = (s_lastSeqId.TryGetValue(attacker, out var cur) ? cur : 0) + 1;
            }
            s_prevGrounded[attacker] = groundedNow;
        }

        return s_lastSeqId.TryGetValue(attacker, out var id) ? id : 0;
    }

    bool GetCtrlNow() {
        if (metaIsCtrl) return true;
        if (metaAttacker == null) return false;
        var attCtrls = metaAttacker.GetComponent<simpleControls>();
        return attCtrls != null && attCtrls.StallHeld();
    }

    void TryHit(Collider2D other) {
        if (other == null || !IsEnemy(other.gameObject)) return;

        var root = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform;
        if (root == null) root = other.transform;

        if (hitThisWindow.Contains(root)) return; // evitar hits duplicados por ventana
        hitThisWindow.Add(root);

        bool isCtrlNow = GetCtrlNow();
        int seqId = GetOrComputeAirSeqId(metaAttacker);

        // 1) Efectos especiales
        if (currentEffect == AttackEffectKind.Launch) {
            var launch = root.GetComponent<simpleLaunch>();
            if (launch != null) launch.ReceiveLaunch();
            return;
        }
        else if (currentEffect == AttackEffectKind.DragToGround) {
            // Volador
            var flyEff = root.GetComponent<simpleFlyingEnemy>();
            if (flyEff != null) {
                flyEff.ReceiveSpecialEffect(areaKind, AttackEffectKind.DragToGround, metaAttacker, seqId);
                return;
            }
            // NUEVO: Enemigo de suelo (solo si está en aire tras launch)
            var groundEff = root.GetComponent<simpleGroundEnemy>();
            if (groundEff != null) {
                groundEff.ReceiveSpecialEffect(areaKind, AttackEffectKind.DragToGround, metaAttacker, seqId);
                return;
            }
            return;
        }

        // 2) Golpe simple (sin efecto especial)
        // Reglas actuales:
        // - Volador: no empujar en HOLD, ni si está en Stun.
        var fly = root.GetComponent<simpleFlyingEnemy>();
        if (fly != null) {
            if (!metaIsHoldTick &&
                fly.state != simpleFlyingEnemy.FlyState.StunnedAir &&
                fly.state != simpleFlyingEnemy.FlyState.StunnedGround) {
                fly.NotifySimpleHit(areaKind, isCtrlNow);
            }
        }

        // (Para enemigos de suelo no tocamos el empuje simple aquí; su lógica de empuje, si existiera, sería propia)

        // 3) Air-stall de enemigo (Ctrl+S / Ctrl+D) – permitido incluso en StunnedAir
        if (isCtrlNow) {
            var stall = root.GetComponent<simpleEnemyAirStall>();
            if (stall != null) stall.TryApplyStall(metaAttacker, seqId);
        }
    }
}
