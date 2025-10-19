using UnityEngine;
using System.Collections.Generic;

public class simpleAttackSensor : MonoBehaviour {
    [Header("Collider objetivo (externo)")]
    public Collider2D targetCollider;

    [Header("Filtro de enemigos")]
    public LayerMask enemyLayers;
    public string enemyTag = "Enemy";

    // ===== Metadatos de la ventana actual (los setea el controller del atacante) =====
    //[Header("Hit Metadata (asignado por el atacante)")]
    //[Tooltip("Quién originó este golpe (player).")]
    [HideInInspector]
    public Transform metaAttacker;

    //[Tooltip("True si el atacante estaba manteniendo Ctrl al disparar esta ventana de S.")]
    [HideInInspector]
    public bool metaIsCtrlS;

    //[Tooltip("Id de 'air sequence' del atacante (aumenta cuando el atacante aterriza).")]
    [HideInInspector]
    public int metaAttackerAirSeqId;

    // Copias activas (congeladas) para la ventana abierta
    Transform _activeAttacker;
    bool _activeIsCtrlS;
    int _activeAttackerAirSeqId;

    // Estado ventana
    bool windowOpen = false;
    AttackEffectKind currentEffect = AttackEffectKind.None;
    readonly HashSet<Transform> hitThisWindow = new HashSet<Transform>();
    static readonly Collider2D[] _overlapBuffer = new Collider2D[32];

    void Awake() {
        if (targetCollider == null)
            targetCollider = GetComponentInChildren<Collider2D>(true);
        if (targetCollider != null) targetCollider.isTrigger = true;
    }

    public void BeginWindow(AttackEffectKind effectKind) {
        currentEffect = effectKind;
        hitThisWindow.Clear();
        windowOpen = true;

        // Congelar metadatos para esta ventana
        _activeAttacker = metaAttacker;
        _activeIsCtrlS = metaIsCtrlS;
        _activeAttackerAirSeqId = metaAttackerAirSeqId;

        ScanOnce();
    }

    public void EndWindow() {
        windowOpen = false;
        hitThisWindow.Clear();
        currentEffect = AttackEffectKind.None;

        // Limpiar activos
        _activeAttacker = null;
        _activeIsCtrlS = false;
        _activeAttackerAirSeqId = 0;
    }

    void FixedUpdate() {
        if (!windowOpen) return;
        ScanOnce();
    }

    void ScanOnce() {
        if (targetCollider == null || !targetCollider.enabled) return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;

        if (enemyLayers.value != 0) {
            filter.SetLayerMask(enemyLayers);
            filter.useLayerMask = true;
        }
        else {
            filter.useLayerMask = false;
        }

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

    void TryHit(Collider2D other) {
        if (other == null || !IsEnemy(other.gameObject)) return;

        var key = other.transform;
        if (hitThisWindow.Contains(key)) return;
        hitThisWindow.Add(key);

        // 1) Efectos de ataque del área
        if (currentEffect == AttackEffectKind.Launch) {
            var receiver = other.GetComponentInParent<simpleLaunch>();
            if (receiver != null) receiver.ReceiveLaunch();
        }

        // 2) Notificar al ENEMIGO (él decide si se stallea o no)
        var enemyStall = other.GetComponentInParent<simpleEnemyAirStall>();
        if (enemyStall != null) {
            enemyStall.ApplyStallFromHit(_activeAttacker, _activeAttackerAirSeqId, _activeIsCtrlS, -1f);
        }
    }
}
