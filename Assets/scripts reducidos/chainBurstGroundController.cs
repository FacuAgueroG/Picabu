using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ChainBurstGroundController : MonoBehaviour {
    [Header("Who launches the burst")]
    public Transform chainAnchor;           // Centro del radio (normalmente (0,0) del Player)
    public LayerMask enemyMask;
    public simpleControls controlsToLock;   // <<— Bloqueo de input del player

    [Header("Detection")]
    public float detectRadius = 4.0f;

    [Header("Pull settings")]
    [Tooltip("Velocidad a la que los enemigos se acercan (u/s).")]
    public float pullSpeed = 18f;
    [Tooltip("Distancia fija que recorre cada enemigo hacia el jugador en el PULL.")]
    public float pullDistance = 1.10f;
    [Tooltip("Pausa breve tras el pull antes del repel.")]
    public float yankPause = 0.06f;

    [Header("Repel settings (duration)")]
    [Tooltip("Duración del EMPUJE (tanto suelo como aire).")]
    public float repelDuration = 0.25f;

    [Header("Repel settings (Ground)")]
    [Tooltip("Distancia horizontal a alejar (siempre alejándose del jugador).")]
    public float groundRepelDistance = 4.0f;

    [Header("Repel settings (Air)")]
    [Tooltip("Distancia horizontal a alejar (siempre alejándose del jugador).")]
    public float airRepelDistanceX = 3.0f;
    [Tooltip("Distancia vertical (positiva = hacia arriba).")]
    public float airRepelDistanceY = 1.5f;

    [Header("Chain visuals")]
    public GameObject chainPrefab;          // Prefab con pivot a la izquierda
    public int chainPoolSize = 12;

    [Header("Chain timing (repel)")]
    [Tooltip("Tiempo que las cadenas permanecen visibles durante el EMPUJE (repel).")]
    public float chainHangDuringRepel = 0.12f; // D

    // runtime
    private readonly List<ChainVictimDriver> _drivers = new();
    private GameObjectPool _chainPool;

    // sync de desbloqueo
    private int _driversActive = 0;

    void Awake() {
        if (chainAnchor == null) chainAnchor = transform;
        if (_chainPool == null) _chainPool = new GameObjectPool(chainPrefab, chainPoolSize, transform);
    }

    /// <summary> Llamado por el combo DDD(hold)+S en suelo. </summary>
    public void Fire() {
        // bloquear input APENAS se dispara el combo (D+S dentro de DDD(Hold))
        if (controlsToLock != null) controlsToLock.blockInput = true;

        PrepareDrivers();
        if (_drivers.Count == 0) {
            // nada que hacer -> liberar control
            if (controlsToLock != null) controlsToLock.blockInput = false;
            return;
        }

        _driversActive = _drivers.Count;

        for (int i = 0; i < _drivers.Count; i++) {
            var d = _drivers[i];
            d.Begin(
                playerAnchor: chainAnchor,
                yankPause: yankPause,
                pullSpeed: pullSpeed,
                pullDistance: pullDistance,
                repelDuration: repelDuration,
                groundRepelDistance: groundRepelDistance,
                airRepelDistanceX: airRepelDistanceX,
                airRepelDistanceY: airRepelDistanceY,
                chainPool: _chainPool,
                chainHangDuringRepel: chainHangDuringRepel,
                owner: this  // para callback de liberación de control
            );
        }
    }

    // --- Wrapper para compatibilidad con tu simpleCombatController ---
    public void TriggerBurstInstance3() {
        Fire();
    }

    private void PrepareDrivers() {
        _drivers.Clear();

        Vector2 center = chainAnchor.position;
        var hits = Physics2D.OverlapCircleAll(center, detectRadius, enemyMask);

        for (int i = 0; i < hits.Length; i++) {
            var col = hits[i];
            if (col.attachedRigidbody == null) continue;

            // limpiar driver anterior (permitir re-empujar)
            var old = col.GetComponent<ChainVictimDriver>();
            if (old != null) Destroy(old);

            var drv = col.gameObject.AddComponent<ChainVictimDriver>();
            drv.ConfigureForCollider(col);
            _drivers.Add(drv);
        }
    }

    // llamado por cada driver cuando APAGÓ su cadena del repel (fin de D por enemigo)
    public void NotifyRepelChainOff() {
        _driversActive = Mathf.Max(0, _driversActive - 1);
        if (_driversActive == 0) {
            // todas las cadenas apagadas -> liberar control del jugador
            if (controlsToLock != null) controlsToLock.blockInput = false;
        }
    }

    void OnDrawGizmosSelected() {
        if (chainAnchor == null) chainAnchor = transform;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(chainAnchor.position, detectRadius);
    }
}
