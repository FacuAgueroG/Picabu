using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Driver temporal y REUTILIZABLE para el Aerial Burst.
/// Corrección importante:
///  - ResetForReuse(): permite volver a usar el mismo componente entre ejecuciones.
///  - FinishAndDestroy(): asegura restaurar BodyType/gravedad y destruye el componente.
///  - Nunca se intenta agregar un segundo driver al mismo enemigo.
/// </summary>
[DisallowMultipleComponent]
public class AerialBurstVictimDriver : MonoBehaviour {
    Rigidbody2D _rb;
    Collider2D _col;
    simpleEnemyState _state;
    simpleFlyingEnemy _flying;
    simpleEnemyGravity2D _grav;
    Transform _player;

    bool _isAirEnemy = false;
    float _yOrigin = 0f;

    // Para restauración
    RigidbodyType2D _prevBodyType = RigidbodyType2D.Dynamic;
    float _prevGravityScale = 1f;

    bool _suspendedAfterLift = false;

    void Awake() {
        CacheComponents();
    }

    void CacheComponents() {
        if (_col == null) _col = GetComponent<Collider2D>();
        if (_rb == null) _rb = GetComponent<Rigidbody2D>();
        if (_state == null) _state = GetComponent<simpleEnemyState>();
        if (_flying == null) _flying = GetComponent<simpleFlyingEnemy>();
        if (_grav == null) _grav = GetComponent<simpleEnemyGravity2D>();
    }

    public void ResetForReuse(Transform playerCenter) {
        CacheComponents();

        _player = playerCenter;
        _isAirEnemy = (_state != null && _state.kind == EnemyKind.Air) || (_flying != null);
        _yOrigin = CurrentPos().y;

        // Cancelar cualquier movimiento previo
        StopAllCoroutines();

        // Si quedó “suspendido” por un run anterior que se interrumpió, restaurar
        if (_suspendedAfterLift) RestoreDynamic();
        _suspendedAfterLift = false;

        // Cancelar perfiles forzados de gravedad si hubiera
        if (_grav != null) _grav.CancelForceMaxFallUntilGround();
    }

    public void Stage1(float pushAirX, float liftGroundY, float timeSec, Action onDone) {
        StartCoroutine(Co_Stage1(pushAirX, liftGroundY, timeSec, onDone));
    }

    IEnumerator Co_Stage1(float pushAirX, float liftGroundY, float timeSec, Action onDone) {
        if (_grav != null) _grav.CancelForceMaxFallUntilGround();

        if (_isAirEnemy) {
            yield return MoveHorizontalAway(pushAirX, timeSec);
        }
        else {
            yield return MoveVertical(+Mathf.Abs(liftGroundY), timeSec); // subir
            SuspendRigidBody();        // queda flotando hasta Stage2
            _suspendedAfterLift = true;
        }

        onDone?.Invoke();
    }

    public void Stage2(float pushAirX_R, float pushGroundG, float timeSec, Action onDone) {
        StartCoroutine(Co_Stage2(pushAirX_R, pushGroundG, timeSec, onDone));
    }

    IEnumerator Co_Stage2(float pushAirX_R, float pushGroundG, float timeSec, Action onDone) {
        if (_grav != null) _grav.CancelForceMaxFallUntilGround();

        if (_isAirEnemy) {
            yield return MoveHorizontalAway(pushAirX_R, timeSec);
        }
        else {
            float dirX = DirAwayFromPlayerX();
            Vector2 start = CurrentPos();
            Vector2 end = new Vector2(start.x + dirX * Mathf.Abs(pushGroundG), _yOrigin);

            var prevType = _rb.bodyType;
            _rb.bodyType = RigidbodyType2D.Kinematic;

            float t = 0f;
            while (t < 1f) {
                t += Time.deltaTime / Mathf.Max(0.0001f, timeSec);
                Vector2 next = Vector2.Lerp(start, end, Mathf.Clamp01(t));
                _rb.MovePosition(next);
                yield return null;
            }

            _rb.bodyType = prevType;
            _suspendedAfterLift = false; // ya volvió a Y0
        }

        onDone?.Invoke();
    }

    // === Limpieza explícita llamada por el Controller (en finally) ===
    public void FinishAndDestroy() {
        StopAllCoroutines();

        if (_suspendedAfterLift) {
            // Si algo falló y seguía suspendido, restaurar
            RestoreDynamic();
            _suspendedAfterLift = false;
        }

        // Cancelar perfiles forzados
        if (_grav != null) _grav.CancelForceMaxFallUntilGround();

        // Para voladores “de sistema”, evitar que queden retornando raro
        if (_flying != null) {
            _flying.StopReturn();
            // No tocamos initialPosition aquí; lo hicimos al mover horizontalmente.
        }

        Destroy(this);
    }

    // ===== Helpers de movimiento =====

    IEnumerator MoveHorizontalAway(float distX, float timeSec) {
        float dir = DirAwayFromPlayerX();
        Vector2 start = CurrentPos();
        Vector2 end = start + new Vector2(dir * Mathf.Abs(distX), 0f);

        var prevType = _rb.bodyType;
        _rb.bodyType = RigidbodyType2D.Kinematic;

        float t = 0f;
        while (t < 1f) {
            t += Time.deltaTime / Mathf.Max(0.0001f, timeSec);
            Vector2 next = Vector2.Lerp(start, end, Mathf.Clamp01(t));
            _rb.MovePosition(next);
            yield return null;
        }

        _rb.bodyType = prevType;

        if (_flying != null) {
            _flying.StopReturn();
            _flying.initialPosition = _rb.position;
        }
    }

    IEnumerator MoveVertical(float distYUp, float timeSec) {
        Vector2 start = CurrentPos();
        Vector2 end = start + Vector2.up * Mathf.Abs(distYUp);

        var prevType = _rb.bodyType;
        _rb.bodyType = RigidbodyType2D.Kinematic;

        float t = 0f;
        while (t < 1f) {
            t += Time.deltaTime / Mathf.Max(0.0001f, timeSec);
            Vector2 next = Vector2.Lerp(start, end, Mathf.Clamp01(t));
            _rb.MovePosition(next);
            yield return null;
        }

        _rb.bodyType = prevType;
    }

    float DirAwayFromPlayerX() {
        if (_player == null || _col == null) return 1f;
        float dir = Mathf.Sign(_col.bounds.center.x - _player.position.x);
        return (dir == 0f) ? 1f : dir;
    }

    Vector2 CurrentPos() {
        if (_rb != null) return _rb.position;
        return (Vector2)transform.position;
    }

    void SuspendRigidBody() {
        if (_rb == null) return;

        _prevBodyType = _rb.bodyType;
        _prevGravityScale = _rb.gravityScale;

        _rb.linearVelocity = Vector2.zero;
        _rb.gravityScale = 0f;
        _rb.bodyType = RigidbodyType2D.Kinematic;
    }

    void RestoreDynamic() {
        if (_rb == null) return;

        _rb.bodyType = _prevBodyType;
        _rb.gravityScale = _prevGravityScale;
        // No seteamos velocity aquí; lo decide la física al final del burst.
    }
}
