using UnityEngine;

public enum EnemyKind { Ground, Air }
public enum EnemyAirState { OnGround, InAir }

[RequireComponent(typeof(Rigidbody2D))]
public class simpleEnemyState : MonoBehaviour {
    [Header("Tipo del enemigo")]
    public EnemyKind kind = EnemyKind.Ground;

    [Header("Detección de suelo (solo Ground)")]
    public simpleEnemyGround2D groundCheck;

    [Header("Estado (solo lectura)")]
    public EnemyAirState airState = EnemyAirState.OnGround;

    Rigidbody2D rb;

    void Reset() {
        groundCheck = GetComponent<simpleEnemyGround2D>();
    }

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        if (groundCheck == null) groundCheck = GetComponent<simpleEnemyGround2D>();
    }

    void FixedUpdate() {
        if (kind == EnemyKind.Air) {
            // Los enemigos "de aire" permanecen en InAir por diseño (no lanzables).
            airState = EnemyAirState.InAir;
            return;
        }

        // Enemigos de tierra: estado depende del suelo.
        bool grounded = (groundCheck != null) && groundCheck.IsGrounded();
        airState = grounded ? EnemyAirState.OnGround : EnemyAirState.InAir;
    }

    /// <summary>
    /// Solo se puede lanzar si es de tierra y está en suelo.
    /// </summary>
    public bool CanBeLaunched() {
        return kind == EnemyKind.Ground && airState == EnemyAirState.OnGround;
    }

    /// <summary>
    /// Llamar cuando se aplique el Launch correctamente.
    /// </summary>
    public void NotifyLaunched() {
        if (kind == EnemyKind.Ground) airState = EnemyAirState.InAir;
    }
}
