using UnityEngine;
using System.Collections.Generic;

public class simpleAttackSensor : MonoBehaviour {
    [Header("Collider objetivo (externo)")]
    [Tooltip("Asigna aquí el Circle/Square Collider2D del hijo (p.ej. 'Circle').")]
    public Collider2D targetCollider;   // <- asigna el collider del hijo

    [Header("Filtro de enemigos")]
    public LayerMask enemyLayers;       // recomendado
    public string enemyTag = "Enemy";   // opcional (vacío para ignorar tag)

    // Estado de ventana
    bool windowOpen = false;
    readonly HashSet<Transform> hitThisWindow = new HashSet<Transform>();

    // Buffer reutilizable para Overlap (evita allocs)
    static readonly Collider2D[] _overlapBuffer = new Collider2D[32];

    void Awake() {
        if (targetCollider == null) {
            // Intento auto-detectar en hijos por si te olvidás de asignar
            targetCollider = GetComponentInChildren<Collider2D>(true);
        }

        if (targetCollider == null)
            Debug.LogWarning("[simpleAttackSensor2D] Falta 'targetCollider' asignado.");
        else
            targetCollider.isTrigger = true; // por si acaso
    }

    // Llamado por simpleAttackArea2D
    public void BeginWindow() {
        hitThisWindow.Clear();
        windowOpen = true;

        // Escaneo inmediato por si ya hay algo dentro al habilitar
        ScanOnce();
    }

    public void EndWindow() {
        windowOpen = false;
        hitThisWindow.Clear();
    }

    void FixedUpdate() {
        if (!windowOpen) return;
        ScanOnce();
    }

    void ScanOnce() {
        if (targetCollider == null || !targetCollider.enabled) return;

        // Filtro
        ContactFilter2D filter = ContactFilter2D.noFilter;
        if (enemyLayers.value != 0) filter.SetLayerMask(enemyLayers);
        filter.useTriggers = true;

        int count = targetCollider.Overlap(filter, _overlapBuffer);
        for (int i = 0; i < count && i < _overlapBuffer.Length; i++) {
            var other = _overlapBuffer[i];
            _overlapBuffer[i] = null; // limpiar (opcional)
            TryHit(other);
        }
    }

    bool IsEnemy(GameObject go) {
        bool byLayer = (enemyLayers.value & (1 << go.layer)) != 0;
        bool byTag = (!string.IsNullOrEmpty(enemyTag) && go.CompareTag(enemyTag));
        return byLayer || byTag;
    }

    void TryHit(Collider2D other) {
        if (other == null) return;
        if (!IsEnemy(other.gameObject)) return;

        var receiver = other.GetComponentInParent<simpleLaunch>();
        if (receiver == null) return;

        var key = receiver.transform;
        if (hitThisWindow.Contains(key)) return;

        hitThisWindow.Add(key);
        receiver.ReceiveLaunch();
        Debug.Log("Golpe (launch) a: " + receiver.name);
    }
}
