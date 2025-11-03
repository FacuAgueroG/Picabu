using UnityEngine;

/// <summary>
/// Componente mínimo para una cadena: estira y orienta un Sprite (pivot Left Center)
/// desde un 'anchor' hasta un 'target'. No mueve objetivos.
/// </summary>
[DisallowMultipleComponent]
public class ChainView : MonoBehaviour {
    public Transform anchor;
    public Transform target;

    SpriteRenderer _sr;
    float _baseWidthWorld = 1f; // ancho 1 unidad => escala.x = longitud
    int _sortingOriginal;
    bool _hasSortingOverride = false;

    void Awake() {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null) {
            _sortingOriginal = _sr.sortingOrder;

            if (_sr.sprite != null) {
                float w = _sr.sprite.rect.width;
                float ppu = _sr.sprite.pixelsPerUnit;
                float localX = transform.localScale.x == 0 ? 1f : transform.localScale.x;
                _baseWidthWorld = (w / Mathf.Max(1f, ppu)) * localX;
            }
        }
    }

    public void Attach(Transform anchor, Transform target, int sortingOverride = -999) {
        this.anchor = anchor;
        this.target = target;

        transform.position = (anchor != null) ? anchor.position : transform.position;

        if (_sr != null) {
            if (sortingOverride != -999) {
                _sr.sortingOrder = sortingOverride;
                _hasSortingOverride = true;
            }
            else {
                _sr.sortingOrder = _sortingOriginal;
                _hasSortingOverride = false;
            }

            if (!_sr.enabled) _sr.enabled = true;
        }

        Refresh();
    }

    public void Detach() {
        anchor = null;
        target = null;
        if (_sr != null && _sr.enabled) _sr.enabled = false;
        if (_sr != null && _hasSortingOverride) {
            _sr.sortingOrder = _sortingOriginal;
            _hasSortingOverride = false;
        }
    }

    /// <summary>
    /// Recalcula ángulo y escala (largo) para conectar anchor->target.
    /// </summary>
    public void Refresh() {
        if (anchor == null || target == null) return;

        Vector3 a = anchor.position;
        Vector3 b = target.GetComponent<Collider2D>() != null
            ? (Vector3)target.GetComponent<Collider2D>().bounds.center
            : target.position;

        Vector2 dir = (b - a);
        float len = dir.magnitude;

        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.SetPositionAndRotation(a, Quaternion.Euler(0f, 0f, ang));

        if (_baseWidthWorld <= 0.0001f) _baseWidthWorld = 1f;
        Vector3 sc = transform.localScale;
        sc.x = len / _baseWidthWorld;
        transform.localScale = sc;
    }
}
