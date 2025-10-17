using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sonda cruda de tiempos: registra cada KeyDown/KeyUp de S y D,
/// midiendo duraciones y gaps sin ningún umbral ni clasificación.
/// - S: simulá tu "quick"
/// - D: simulá tu "slow/mini-hold"
/// Controles:
///   Enter  -> imprime resumen de ambas rutas (S y D)
///   R      -> resetea los datos
///   M      -> agrega un "marker" manual con timestamp (opcional)
///   A      -> alterna un flag "AirContext" (solo información; se loguea en cada Down)
/// 
/// Nota: si presionás Enter mientras una tecla sigue apretada, se cerrará
/// esa pulsación al 'now' para poder medir su duración.
/// </summary>
public class Pruebas : MonoBehaviour {
    [Header("Teclas a medir")]
    public KeyCode keyS = KeyCode.S;   // tu "quick"
    public KeyCode keyD = KeyCode.D;   // tu "slow" (si querés simular mini-holds)

    [Header("Opcional: contexto")]
    [Tooltip("Solo informativo: lo podés alternar con la tecla A en runtime.")]
    public bool airContext = false;

    private class Press {
        public int index;                    // ordinal dentro de la ruta
        public float downTime;               // Time.realtimeSinceStartup (s)
        public float upTime;                 // idem; -1 si aún no soltó
        public float durationMs;             // (up - down)*1000
        public float gapSincePrevDownMs;     // (down_i - down_{i-1})*1000
        public float gapSincePrevUpMs;       // (down_i - up_{i-1})*1000
        public bool airAtDown;               // snapshot del flag airContext al Down
    }

    private class Route {
        public string label;
        public List<Press> presses = new List<Press>();
        public bool isDown = false;
        public float currentDownTime = -1f;
        public List<string> markers = new List<string>(); // marcas manuales con tiempo relativo
        public float startTime; // para imprimir tiempos relativos
    }

    private Route routeS;
    private Route routeD;

    void Awake() {
        ResetRoutes();
    }

    void ResetRoutes() {
        float now = Time.realtimeSinceStartup;
        routeS = new Route { label = "S", startTime = now };
        routeD = new Route { label = "D", startTime = now };
    }

    void Update() {
        float now = Time.realtimeSinceStartup;

        // ------- Captura S -------
        if (Input.GetKeyDown(keyS)) {
            routeS.isDown = true;
            routeS.currentDownTime = now;

            var p = new Press {
                index = routeS.presses.Count,
                downTime = now,
                upTime = -1f,
                durationMs = 0f,
                airAtDown = airContext,
                gapSincePrevDownMs = routeS.presses.Count > 0 ? (now - routeS.presses[^1].downTime) * 1000f : 0f,
                gapSincePrevUpMs = routeS.presses.Count > 0 ? (now - routeS.presses[^1].upTime) * 1000f : 0f,
            };
            routeS.presses.Add(p);
        }
        if (routeS.isDown && Input.GetKeyUp(keyS)) {
            routeS.isDown = false;
            var p = routeS.presses[^1];
            if (p.upTime < 0f) {
                p.upTime = now;
                p.durationMs = Mathf.Max(0f, (p.upTime - p.downTime) * 1000f);
            }
        }

        // ------- Captura D -------
        if (Input.GetKeyDown(keyD)) {
            routeD.isDown = true;
            routeD.currentDownTime = now;

            var p = new Press {
                index = routeD.presses.Count,
                downTime = now,
                upTime = -1f,
                durationMs = 0f,
                airAtDown = airContext,
                gapSincePrevDownMs = routeD.presses.Count > 0 ? (now - routeD.presses[^1].downTime) * 1000f : 0f,
                gapSincePrevUpMs = routeD.presses.Count > 0 ? (now - routeD.presses[^1].upTime) * 1000f : 0f,
            };
            routeD.presses.Add(p);
        }
        if (routeD.isDown && Input.GetKeyUp(keyD)) {
            routeD.isDown = false;
            var p = routeD.presses[^1];
            if (p.upTime < 0f) {
                p.upTime = now;
                p.durationMs = Mathf.Max(0f, (p.upTime - p.downTime) * 1000f);
            }
        }

        // ------- Hotkeys de utilidad -------
        if (Input.GetKeyDown(KeyCode.Return)) // Enter => imprime ambas rutas
        {
            // Si hay pulsaciones activas, cerrarlas "ahora" para poder medir duración
            CloseOpenPressIfAny(routeS, now);
            CloseOpenPressIfAny(routeD, now);

            PrintRoute(routeS);
            PrintRoute(routeD);

            Debug.Log("---- Copiá/pegá este bloque acá en el chat para analizar buffers/activeTime/CD ----");
        }

        if (Input.GetKeyDown(KeyCode.R)) // reset
        {
            ResetRoutes();
            Debug.Log("[ProbeRaw] Reset de datos.");
        }

        if (Input.GetKeyDown(KeyCode.M)) // marker manual
        {
            float t = now;
            string mark = $"MARK @ +{(t - routeS.startTime) * 1000f:F1}ms (S-ref), +{(t - routeD.startTime) * 1000f:F1}ms (D-ref)";
            routeS.markers.Add(mark);
            routeD.markers.Add(mark);
            Debug.Log("[ProbeRaw] " + mark);
        }

        if (Input.GetKeyDown(KeyCode.A)) // alterna contexto "aire" (solo informativo)
        {
            airContext = !airContext;
            Debug.Log("[ProbeRaw] AirContext = " + (airContext ? "ON" : "OFF"));
        }
    }

    void CloseOpenPressIfAny(Route r, float now) {
        if (!r.isDown) return;
        if (r.presses.Count == 0) return;

        var p = r.presses[^1];
        if (p.upTime < 0f) {
            p.upTime = now;
            p.durationMs = Mathf.Max(0f, (p.upTime - p.downTime) * 1000f);
        }
        r.isDown = false;
        r.currentDownTime = -1f;
    }

    void PrintRoute(Route r) {
        Debug.Log($"================= PROBE RAW [{r.label}] =================");
        Debug.Log($"Total presses: {r.presses.Count}");

        if (r.markers.Count > 0) {
            Debug.Log("Markers:");
            foreach (var m in r.markers) Debug.Log("  • " + m);
        }

        if (r.presses.Count == 0) {
            Debug.Log("Sin datos.");
            Debug.Log("=========================================================");
            return;
        }

        float minDur = float.MaxValue, maxDur = 0f, sumDur = 0f;
        float minGapDown = float.MaxValue, maxGapDown = 0f;
        float minGapUp = float.MaxValue, maxGapUp = 0f;

        for (int i = 0; i < r.presses.Count; i++) {
            var p = r.presses[i];
            sumDur += p.durationMs;
            if (p.durationMs < minDur) minDur = p.durationMs;
            if (p.durationMs > maxDur) maxDur = p.durationMs;

            if (i > 0) {
                if (p.gapSincePrevDownMs < minGapDown) minGapDown = p.gapSincePrevDownMs;
                if (p.gapSincePrevDownMs > maxGapDown) maxGapDown = p.gapSincePrevDownMs;

                if (p.gapSincePrevUpMs < minGapUp) minGapUp = p.gapSincePrevUpMs;
                if (p.gapSincePrevUpMs > maxGapUp) maxGapUp = p.gapSincePrevUpMs;
            }
        }

        float avgDur = sumDur / r.presses.Count;

        Debug.Log($"Duración (ms): min={minDur:F1}  max={maxDur:F1}  avg={avgDur:F1}");
        if (r.presses.Count > 1) {
            Debug.Log($"Gaps Down→Down (ms): min={minGapDown:F1}  max={maxGapDown:F1}");
            Debug.Log($"Gaps Up→Down (ms):  min={minGapUp:F1}    max={maxGapUp:F1}");
        }

        for (int i = 0; i < r.presses.Count; i++) {
            var p = r.presses[i];
            float relDownMs = (p.downTime - r.startTime) * 1000f;
            float relUpMs = p.upTime > 0f ? (p.upTime - r.startTime) * 1000f : -1f;
            string upStr = p.upTime > 0f ? $"{relUpMs:F1}ms" : "OPEN";
            string air = p.airAtDown ? "AIR" : "GROUND";
            Debug.Log($"  #{i}  down@{relDownMs:F1}ms  up@{upStr}  dur={p.durationMs:F1}ms  gapDD={p.gapSincePrevDownMs:F1}  gapUD={p.gapSincePrevUpMs:F1}  ctx={air}");
        }

        Debug.Log("=========================================================");
    }
}
