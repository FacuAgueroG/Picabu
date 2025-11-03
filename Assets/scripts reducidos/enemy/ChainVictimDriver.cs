using System.Collections;
using UnityEngine;

/// <summary>
/// Driver TEMPORAL por víctima: Pull (distancia fija desde su posición inicial) -> pausa -> Repel por distancia/tiempo.
/// Durante el REPEL, la cadena queda fija y se apaga a los D segundos. Al apagarse, avisa al owner.
/// Se AUTODESTRUYE al terminar, para poder repetir el combo sin quedar pegado.
/// </summary>
[DisallowMultipleComponent]
public class ChainVictimDriver : MonoBehaviour {
    // refs
    Rigidbody2D _rb;
    Collider2D _col;
    simpleEnemyState _enemyState;
    simpleFlyingEnemy _flying;
    simpleEnemyGravity2D _eGravity;
    GameObjectPool _pool;
    Transform _playerAnchor;
    ChainBurstGroundController _owner;

    // params
    float _pullSpeed;        // velocidad del pull (u/s)
    float _pullDist;         // distancia fija a recorrer hacia el jugador
    float _pause;            // yank pause tras pull

    float _repelDuration;    // R: segundos
    float _gRepelDist;       // suelo: distancia horizontal total
    float _aRepelX;          // aire: distancia X total (alejándose del player)
    float _aRepelY;          // aire: altura pico (>=0). Se vuelve a y0 al final.

    float _chainHangDuringRepel; // D

    // chain visual
    GameObject _chainGO;
    Transform _chainT;
    SpriteRenderer _chainSR;
    bool _freezeChainVisualDuringRepel = false; // cuando true, deja de seguir al enemigo
    float _chainBaseWidthUnits = 1f;            // ancho del sprite en unidades de mundo (bounds.x a escala 1)

    // bookkeeping
    bool _isAir;

    public void ConfigureForCollider(Collider2D col) {
        _col = col;
        _rb = col.attachedRigidbody;
        _enemyState = col.GetComponent<simpleEnemyState>();
        _flying = col.GetComponent<simpleFlyingEnemy>();
        _eGravity = col.GetComponent<simpleEnemyGravity2D>();

        _isAir = (_enemyState != null && _enemyState.kind == EnemyKind.Air) || _flying != null;
    }

    public void Begin(Transform playerAnchor,
                      float yankPause,
                      float pullSpeed,
                      float pullDistance,
                      float repelDuration,
                      float groundRepelDistance,
                      float airRepelDistanceX,
                      float airRepelDistanceY,
                      GameObjectPool chainPool,
                      float chainHangDuringRepel,
                      ChainBurstGroundController owner) {
        _playerAnchor = playerAnchor;
        _pause = Mathf.Max(0f, yankPause);

        _pullSpeed = Mathf.Max(0.01f, pullSpeed);
        _pullDist = Mathf.Max(0f, pullDistance);

        _repelDuration = Mathf.Max(0.01f, repelDuration);
        _gRepelDist = Mathf.Max(0f, groundRepelDistance);
        _aRepelX = airRepelDistanceX; // signo se define respecto al jugador
        _aRepelY = Mathf.Max(0f, airRepelDistanceY); // altura pico; termina en y0

        _pool = chainPool;
        _chainHangDuringRepel = Mathf.Max(0f, chainHangDuringRepel);

        _owner = owner;

        StopAllCoroutines();
        StartCoroutine(Co_Run());
    }

    IEnumerator Co_Run() {
        if (_eGravity != null) _eGravity.CancelForceMaxFallUntilGround();

        SpawnOrReuseChain();

        // ===== PULL =====
        yield return Co_PullTowardPlayer(_pullDist);

        // ===== Pause =====
        if (_pause > 0f) yield return new WaitForSeconds(_pause);

        // ===== REPEL =====
        _freezeChainVisualDuringRepel = true; // cadenas fijas durante el empuje

        if (_isAir) yield return Co_RepelAir_ByParabola();
        else yield return Co_RepelGround_ByDistanceTime();

        // ===== Cadenas durante REPEL (apagado tras D) =====
        if (_chainHangDuringRepel > 0f)
            yield return new WaitForSeconds(_chainHangDuringRepel);

        // Apagar cadenas y notificar al owner (para liberar control cuando todas apaguen)
        DespawnChain();
        _owner?.NotifyRepelChainOff();

        FinishAndDestroy();
    }

    // ---------- Pull: recorre una distancia fija desde su posición inicial, a velocidad constante ----------
    IEnumerator Co_PullTowardPlayer(float pullStep) {
        Vector2 playerC = PlayerBoundsCenter();
        Vector2 enemyInit = _col.bounds.center; // inicio del pull
        Vector2 cur = _rb.position;

        Vector2 toPlayer = (playerC - enemyInit);
        float distToPlayer = toPlayer.magnitude;

        if (distToPlayer < 0.0001f || pullStep <= 0f) {
            UpdateChainVisual(playerC, cur);
            yield break;
        }

        Vector2 dir = toPlayer / distToPlayer;
        float step = Mathf.Min(pullStep, distToPlayer);
        Vector2 target = enemyInit + dir * step;

        var prevType = _rb.bodyType;
        _rb.bodyType = RigidbodyType2D.Kinematic;

        const float epsilon = 0.005f;
        int safety = 0;
        while (((Vector2)_rb.position - target).sqrMagnitude > (epsilon * epsilon) && safety++ < 2000) {
            cur = _rb.position;
            Vector2 next = Vector2.MoveTowards(cur, target, _pullSpeed * Time.deltaTime);
            _rb.MovePosition(next);

            if (!_freezeChainVisualDuringRepel)
                UpdateChainVisual(playerC, next);
            yield return null;
        }

        _rb.bodyType = prevType;
    }

    // ---------- Repel (GROUND): alejar N (horizontal) en R tiempo ----------
    IEnumerator Co_RepelGround_ByDistanceTime() {
        var prevType = _rb.bodyType;
        _rb.bodyType = RigidbodyType2D.Kinematic;

        Vector2 start = _rb.position;
        float dirX = Mathf.Sign(_col.bounds.center.x - PlayerBoundsCenter().x);
        if (dirX == 0f) dirX = 1f;

        Vector2 target = start + new Vector2(dirX * _gRepelDist, 0f);

        float t = 0f;
        while (t < 1f) {
            t += Time.deltaTime / _repelDuration;
            Vector2 next = Vector2.Lerp(start, target, Mathf.Clamp01(t));
            _rb.MovePosition(next);

            // cadenas fijas -> NO actualizamos durante repel
            yield return null;
        }

        _rb.bodyType = prevType;
    }

    // ---------- Repel (AIR): parábola en R segundos (X lineal, Y sube a pico y vuelve a y0) ----------
    IEnumerator Co_RepelAir_ByParabola() {
        var prevType = _rb.bodyType;
        _rb.bodyType = RigidbodyType2D.Kinematic;

        Vector2 start = _rb.position;
        float y0 = start.y;

        float dirX = Mathf.Sign(_col.bounds.center.x - PlayerBoundsCenter().x);
        if (dirX == 0f) dirX = 1f;

        float dx = dirX * Mathf.Abs(_aRepelX); // siempre alejándose del player
        float h = _aRepelY;                   // altura pico (>=0)

        float t = 0f;
        while (t < 1f) {
            t += Time.deltaTime / _repelDuration;
            float tt = Mathf.Clamp01(t);

            float x = start.x + dx * tt;
            float y = y0 + 4f * h * tt * (1f - tt); // nunca baja de y0

            _rb.MovePosition(new Vector2(x, y));

            // cadenas fijas -> NO actualizamos durante repel
            yield return null;
        }

        _rb.MovePosition(new Vector2(start.x + dx, y0));

        if (_flying != null) {
            _flying.initialPosition = _rb.position; // nuevo “punto de origen”
            _flying.StopReturn();
            _flying.ApplyNormalColor();
        }

        if (_enemyState != null && _enemyState.kind == EnemyKind.Air)
            _rb.bodyType = RigidbodyType2D.Kinematic;
        else
            _rb.bodyType = prevType;
    }

    // ---------- Chain Visual helpers (solo escalo X; NO toco drawMode ni scale.Y) ----------
    void SpawnOrReuseChain() {
        if (_pool == null) return;
        _chainGO = _pool.Get();
        if (_chainGO == null) return;

        _chainT = _chainGO.transform;
        _chainSR = _chainGO.GetComponentInChildren<SpriteRenderer>();
        _chainT.gameObject.SetActive(true);

        // Calcular ancho base del sprite en unidades (para escalar X con precisión)
        if (_chainSR != null && _chainSR.sprite != null)
            _chainBaseWidthUnits = Mathf.Max(0.0001f, _chainSR.sprite.bounds.size.x);
        else
            _chainBaseWidthUnits = 1f;

        // al inicio, sí sigue al enemigo (PULL)
        _freezeChainVisualDuringRepel = false;

        UpdateChainVisual(PlayerBoundsCenter(), _col.bounds.center);
    }

    void UpdateChainVisual(Vector2 origin, Vector2 target) {
        if (_chainT == null) return;

        Vector2 dir = (target - origin);
        float len = dir.magnitude;
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // posicionar y rotar desde el origen (pivot izquierda)
        _chainT.position = origin;
        _chainT.rotation = Quaternion.Euler(0, 0, ang);

        // Escalar SOLO en X según longitud; NO tocar Y ni Z (grosor del prefab se respeta)
        if (_chainBaseWidthUnits <= 0f) _chainBaseWidthUnits = 1f;
        float scaleX = len / _chainBaseWidthUnits;

        Vector3 s = _chainT.localScale;
        s.x = scaleX;
        // s.y intacto (grosor original del prefab)
        // s.z intacto
        _chainT.localScale = s;
    }

    void DespawnChain() {
        if (_pool == null || _chainGO == null) return;
        _pool.Release(_chainGO);
        _chainGO = null;
        _chainT = null;
        _chainSR = null;
    }

    void FinishAndDestroy() {
        if (_eGravity != null) _eGravity.CancelForceMaxFallUntilGround();
        Destroy(this); // permite repetir el combo sin quedar pegado
    }

    Vector2 PlayerBoundsCenter() {
        var player = _playerAnchor;
        if (player == null) return transform.position;

        var pc = player.GetComponentInParent<Collider2D>();
        if (pc != null) return pc.bounds.center;
        return player.position;
    }
}
