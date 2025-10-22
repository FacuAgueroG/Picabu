using UnityEngine;
using System.Collections;

public enum AttackEffectKind { None, Launch, DragToGround }

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

    bool HasSeparateVisualGO =>
        areaSpriteGO != null && areaSpriteGO != this.gameObject;

    void Reset() {
        if (areaCollider == null) areaCollider = GetComponent<Collider2D>();
        if (sensor == null) sensor = GetComponent<simpleAttackSensor>();
        if (areaSprite == null) areaSprite = GetComponentInChildren<SpriteRenderer>(true);

        if (areaSpriteGO == null && areaSprite != null) {
            areaSpriteGO = areaSprite.gameObject;
        }
        if (areaCollider != null) areaCollider.isTrigger = true;
    }

    void Awake() {
        if (areaCollider != null) areaCollider.enabled = false;

        if (areaSprite != null) areaSprite.enabled = false;
        if (HasSeparateVisualGO && areaSpriteGO != null) {
            areaSpriteGO.SetActive(false);
        }

        ActiveUntil = -999f;
        CooldownUntil = -999f;
    }

    public bool IsActiveOrCooling => IsActive || InCooldown;

    // =============== API pública ===============

    // Disparo simple (respeta estado)
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

    /// <summary>
    /// Fuerza una ventana de ataque AHORA MISMO, aunque esté activa/en cooldown/en hold.
    /// - Cierra la ventana actual si la hay.
    /// - Cancela el HOLD si estaba activo.
    /// - Ignora cooldown previo (lo vuelve a aplicar tras la ventana).
    /// </summary>
    public bool ForceFireOnce(AttackEffectKind effectKind) {
        if (!isActiveAndEnabled) return false;

        // Cerrar ventana si estaba activa
        if (IsActive) {
            if (areaCollider != null) areaCollider.enabled = false;
            if (sensor != null) sensor.EndWindow();
            IsActive = false;
        }

        // Cancelar HOLD si estaba
        if (InHold) {
            InHold = false;
            if (HasSeparateVisualGO && areaSpriteGO != null) areaSpriteGO.SetActive(false);
            if (areaSprite != null) areaSprite.enabled = false;
            if (sensor != null) sensor.metaIsHoldTick = false; // asegurar flag off
        }

        // Cancelar coroutines previas
        if (activeRoutine != null) { StopCoroutine(activeRoutine); activeRoutine = null; }
        if (holdRoutine != null) { StopCoroutine(holdRoutine); holdRoutine = null; }

        // Ignorar estado previo de cooldown
        InCooldown = false;

        // Disparar ventana fija
        activeRoutine = StartCoroutine(Co_ActiveFixed(effectKind));
        return true;
    }

    // Iniciar HOLD (si está habilitado)
    public bool StartHold() {
        if (!supportsHold) return false;
        if (InHold || IsActiveOrCooling) return false;
        if (!isActiveAndEnabled) return false;

        InHold = true;

        ShowVisual(true);
        SetSpriteColor(holdBaseColor);

        if (holdRoutine != null) StopCoroutine(holdRoutine);
        holdRoutine = StartCoroutine(Co_HoldLoop());
        return true;
    }

    public void StopHold() {
        if (!InHold) return;

        InHold = false;

        if (IsActive) {
            if (areaCollider != null) areaCollider.enabled = false;
            if (sensor != null) sensor.EndWindow();
            IsActive = false;
        }

        ShowVisual(false);

        if (sensor != null) sensor.metaIsHoldTick = false; // asegurar flag off

        if (holdRoutine != null) {
            StopCoroutine(holdRoutine);
            holdRoutine = null;
        }
    }

    // =============== Coroutines ===============

    IEnumerator Co_ActiveFixed(AttackEffectKind effectKind) {
        IsActive = true;

        ShowVisual(true);
        if (!InHold) SetSpriteColor(Color.white);

        // Esperar a Fixed para coherencia con física
        yield return new WaitForFixedUpdate();

        // Ventana NO-hold: asegurar flag en sensor
        if (sensor != null) sensor.metaIsHoldTick = false;

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

    IEnumerator Co_ActiveSustain(AttackEffectKind effectKind) {
        IsActive = true;

        ShowVisual(true);
        if (!InHold) SetSpriteColor(Color.white);

        yield return new WaitForFixedUpdate();

        // Ventana NO-hold: asegurar flag en sensor
        if (sensor != null) sensor.metaIsHoldTick = false;

        if (sensor != null) sensor.BeginWindow(effectKind);
        if (areaCollider != null) areaCollider.enabled = true;

        float start = Time.time;
        float maxEnd = start + activeTime;
        ActiveUntil = maxEnd;

        while (Time.time < maxEnd && Input.GetKey(sustainKey)) {
            yield return null;
        }

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

            // Ventana HOLD: marcar flag en sensor SOLO durante el tick
            if (sensor != null) sensor.metaIsHoldTick = true;

            if (sensor != null) sensor.BeginWindow(AttackEffectKind.None);
            if (areaCollider != null) areaCollider.enabled = true;
            IsActive = true;

            ActiveUntil = Time.time + activeTime;
            yield return new WaitForSeconds(activeTime);

            // Tick OFF
            if (areaCollider != null) areaCollider.enabled = false;
            if (sensor != null) sensor.EndWindow();
            IsActive = false;

            // Limpiar flag de HOLD en sensor hasta el próximo tick
            if (sensor != null) sensor.metaIsHoldTick = false;

            // Volver a color base HOLD
            SetSpriteColor(holdBaseColor);

            float offTime = Mathf.Max(0f, holdTickInterval - activeTime);
            if (offTime > 0f) yield return new WaitForSeconds(offTime);
            else yield return null;
        }
    }

    // =============== Visual helpers ===============

    void ShowVisual(bool on) {
        if (HasSeparateVisualGO && areaSpriteGO != null) {
            areaSpriteGO.SetActive(on);
        }
        if (areaSprite != null) areaSprite.enabled = on;
    }

    void SetSpriteColor(Color c) {
        if (areaSprite != null) areaSprite.color = c;
    }
}
