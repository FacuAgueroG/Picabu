using UnityEngine;
using System.Collections;

public enum AttackEffectKind { None, Launch }

public class simpleAttack : MonoBehaviour {
    [Header("Referencias")]
    [Tooltip("Collider del hitbox (isTrigger = true). Se enciende/ apaga en runtime.")]
    public Collider2D areaCollider;

    [Tooltip("GameObject SOLO del visual (sprite). Si lo dejas vacío y el sprite está en ESTE mismo GO, se controlará POR RENDERER (no se hará SetActive(false) al GO).")]
    public GameObject areaSpriteGO;

    [Tooltip("SpriteRenderer para cambiar color en ticks/HOLD. Si está en el mismo GO, se controla por renderer.enabled.")]
    public SpriteRenderer areaSprite;

    [Tooltip("Sensor que hace Overlap y aplica efectos (Launch, etc.).")]
    public simpleAttackSensor sensor;

    [Header("Tiempos base")]
    [Min(0.01f)] public float activeTime = 0.20f;
    [Min(0f)] public float cooldownTime = 0.20f;

    [Header("Modo HOLD (solo si esta área lo soporta)")]
    public bool supportsHold = false;
    [Tooltip("Intervalo total del ‘latido’ en HOLD (>= activeTime).")]
    public float holdTickInterval = 0.30f;
    [Tooltip("Color base del sprite mientras está en HOLD.")]
    public Color holdBaseColor = Color.white;
    [Tooltip("Color del sprite durante el frame/tick de lectura en HOLD.")]
    public Color holdTickColor = new Color(1f, 0.85f, 0.85f, 1f);

    [Header("Modo alternativo: ‘activo mientras mantengo’")]
    [Tooltip("Si está activo, el sprite+collider se mantienen encendidos mientras el jugador mantiene la tecla, hasta un máximo = activeTime.")]
    public bool sustainWhileHeld = false;
    [Tooltip("Tecla a leer para el modo ‘activo mientras mantengo’. Para S usa KeyCode.S; para D usa KeyCode.D.")]
    public KeyCode sustainKey = KeyCode.None;

    // Estado
    public bool IsActive { get; private set; } = false;     // ventana activa
    public bool InCooldown { get; private set; } = false;   // en cooldown
    public bool InHold { get; private set; } = false;       // modo hold

    // Tiempos expuestos para controladores externos
    public float ActiveUntil { get; private set; } = -999f;
    public float CooldownUntil { get; private set; } = -999f;

    Coroutine activeRoutine;
    Coroutine holdRoutine;

    // helper: ¿el sprite GO es distinto del GO que tiene este script?
    bool HasSeparateVisualGO =>
        areaSpriteGO != null && areaSpriteGO != this.gameObject;

    void Reset() {
        if (areaCollider == null) areaCollider = GetComponent<Collider2D>();
        if (sensor == null) sensor = GetComponent<simpleAttackSensor>();
        if (areaSprite == null) areaSprite = GetComponentInChildren<SpriteRenderer>(true);

        // Si no nos dieron un GO de sprite explícito, pero hay sprite renderer,
        // usamos el GameObject del renderer para el control visual.
        if (areaSpriteGO == null && areaSprite != null) {
            areaSpriteGO = areaSprite.gameObject;
        }

        if (areaCollider != null) areaCollider.isTrigger = true;
    }

    void Awake() {
        if (areaCollider != null) areaCollider.enabled = false;

        // ⚠️ Nunca apagues ESTE GameObject. Si el visual está en este mismo GO,
        // controla el renderer; si es un hijo independiente, sí podés SetActive(false).
        if (areaSprite != null) areaSprite.enabled = false; // renderer apagado por defecto
        if (HasSeparateVisualGO && areaSpriteGO != null) {
            areaSpriteGO.SetActive(false);
        }

        ActiveUntil = -999f;
        CooldownUntil = -999f;
    }

    public bool IsActiveOrCooling => IsActive || InCooldown;

    // =============== API pública ===============

    // Disparo simple (una vez)
    public bool FireOnce(AttackEffectKind effectKind) {
        if (IsActiveOrCooling || InHold) return false;
        if (!isActiveAndEnabled) return false;

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(
            sustainWhileHeld && sustainKey != KeyCode.None
            ? Co_ActiveSustain(effectKind)
            : Co_ActiveFixed(effectKind)
        );
        return true;
    }

    // Iniciar HOLD (si está habilitado)
    public bool StartHold() {
        if (!supportsHold) return false;
        if (InHold || IsActiveOrCooling) return false;
        if (!isActiveAndEnabled) return false;

        InHold = true;

        // Visual permanente encendido
        ShowVisual(true);
        SetSpriteColor(holdBaseColor);

        if (holdRoutine != null) StopCoroutine(holdRoutine);
        holdRoutine = StartCoroutine(Co_HoldLoop());
        return true;
    }

    public void StopHold() {
        if (!InHold) return;

        InHold = false;

        // Si justo estaba en un tick activo, cerramos
        if (IsActive) {
            if (areaCollider != null) areaCollider.enabled = false;
            if (sensor != null) sensor.EndWindow();
            IsActive = false;
        }

        ShowVisual(false);

        if (holdRoutine != null) {
            StopCoroutine(holdRoutine);
            holdRoutine = null;
        }
    }

    // =============== Coroutines ===============

    // Ventana fija: como siempre (activo por activeTime exacto)
    IEnumerator Co_ActiveFixed(AttackEffectKind effectKind) {
        IsActive = true;

        ShowVisual(true);
        if (!InHold) SetSpriteColor(Color.white);

        // Esperar al próximo FixedUpdate para coherencia física
        yield return new WaitForFixedUpdate();

        if (sensor != null) sensor.BeginWindow(effectKind);
        if (areaCollider != null) areaCollider.enabled = true;

        ActiveUntil = Time.time + activeTime;
        yield return new WaitForSeconds(activeTime);

        if (areaCollider != null) areaCollider.enabled = false;
        if (sensor != null) sensor.EndWindow();

        if (!InHold) ShowVisual(false);

        IsActive = false;
        InCooldown = true;

        CooldownUntil = Time.time + cooldownTime;
        yield return new WaitForSeconds(cooldownTime);

        InCooldown = false;
        activeRoutine = null;
    }

    // Ventana ‘mientras mantengo’: se apaga al soltar o al llegar al tope (=activeTime)
    IEnumerator Co_ActiveSustain(AttackEffectKind effectKind) {
        IsActive = true;

        ShowVisual(true);
        if (!InHold) SetSpriteColor(Color.white);

        yield return new WaitForFixedUpdate();

        if (sensor != null) sensor.BeginWindow(effectKind);
        if (areaCollider != null) areaCollider.enabled = true;

        float start = Time.time;
        float maxEnd = start + activeTime;
        ActiveUntil = maxEnd; // tope teórico

        // Mantener encendido hasta soltar o alcanzar el tope
        while (Time.time < maxEnd && Input.GetKey(sustainKey)) {
            // mantener activo; el sensor hace lecturas en FixedUpdate
            yield return null; // frame a frame (puede ser FixedUpdate, pero aquí es suficiente)
        }

        // Cierre
        if (areaCollider != null) areaCollider.enabled = false;
        if (sensor != null) sensor.EndWindow();

        if (!InHold) ShowVisual(false);

        IsActive = false;
        InCooldown = true;

        CooldownUntil = Time.time + cooldownTime;
        yield return new WaitForSeconds(cooldownTime);

        InCooldown = false;
        activeRoutine = null;
    }

    IEnumerator Co_HoldLoop() {
        var waitFixed = new WaitForFixedUpdate();

        while (InHold) {
            // Tick ON (ventana activa)
            SetSpriteColor(holdTickColor);
            yield return waitFixed;

            if (sensor != null) sensor.BeginWindow(AttackEffectKind.None); // S en hold NO lanza (o ajustá si querés)
            if (areaCollider != null) areaCollider.enabled = true;
            IsActive = true;

            ActiveUntil = Time.time + activeTime;
            yield return new WaitForSeconds(activeTime);

            // Tick OFF
            if (areaCollider != null) areaCollider.enabled = false;
            if (sensor != null) sensor.EndWindow();
            IsActive = false;

            // Volver a color base HOLD
            SetSpriteColor(holdBaseColor);

            float offTime = Mathf.Max(0f, holdTickInterval - activeTime);
            if (offTime > 0f) yield return new WaitForSeconds(offTime);
            else yield return null;
        }
    }

    // =============== Visual helpers ===============

    void ShowVisual(bool on) {
        // Si el visual es un hijo independiente, podemos setActive al hijo.
        if (HasSeparateVisualGO && areaSpriteGO != null) {
            areaSpriteGO.SetActive(on);
        }
        // Siempre controlamos el renderer (si existe) para soportar el caso “mismo GO”.
        if (areaSprite != null) areaSprite.enabled = on;
    }

    void SetSpriteColor(Color c) {
        if (areaSprite != null) areaSprite.color = c;
    }
}
