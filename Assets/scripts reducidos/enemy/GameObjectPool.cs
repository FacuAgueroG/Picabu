using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pool simple para GameObjects (ideal para las cadenas).
/// </summary>
public class GameObjectPool {
    private readonly Queue<GameObject> _q = new();
    private readonly GameObject _prefab;
    private readonly Transform _parent;

    public GameObjectPool(GameObject prefab, int warm, Transform parent) {
        _prefab = prefab;
        _parent = parent;

        if (_prefab == null || warm <= 0) return;

        for (int i = 0; i < warm; i++) {
            var go = Object.Instantiate(_prefab, _parent);
            go.SetActive(false);
            _q.Enqueue(go);
        }
    }

    public GameObject Get() {
        if (_prefab == null) return null;

        if (_q.Count > 0) {
            var go = _q.Dequeue();
            go.SetActive(true);
            return go;
        }
        return Object.Instantiate(_prefab, _parent);
    }

    public void Release(GameObject go) {
        if (go == null) return;
        go.SetActive(false);
        _q.Enqueue(go);
    }
}
