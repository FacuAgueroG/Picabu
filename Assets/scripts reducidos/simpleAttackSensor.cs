using UnityEngine;
using System.Collections.Generic;

public class simpleAttackSensor : MonoBehaviour {
    [Header("Collider objetivo (externo)")]
    [Tooltip("El collider del hitbox que se enciende/apaga (Circle/Box).")]
    public Collider2D targetCollider;

    [Header("Filtro de enemigos")]
    public LayerMask enemyLayers;
    public string enemyTag = "Enemy";

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

        // Filtro robusto
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

        if (currentEffect == AttackEffectKind.Launch) {
            var receiver = other.GetComponentInParent<simpleLaunch>();
            if (receiver != null) receiver.ReceiveLaunch();
        }
        // Aquí podrías añadir daño/poise para currentEffect == None si quieres.
    }
}
