using UnityEngine;

public class simpleCombatController : MonoBehaviour {
    [Header("Referencias")]
    public simpleControls controls;
    public simpleAttack attackSArea;
    public simpleAttack attackDArea;
    public simpleGround2D ground;
    public simpleAirStall airStall;

    [Header("Combo S (base)")]
    public float comboWindowS = 0.50f;
    public float holdTimeS = 0.25f;

    // ================= Buffer S (configurable) =================
    public enum SBufferMode { FullCooldown, TailOnly }

    [Header("Buffer S (en cooldown)")]
    public SBufferMode sBufferMode = SBufferMode.FullCooldown;
    public float bufferTailSeconds = 0.12f;
    public float bufferExpireAfterCd = 0.12f;

    [Header("Buffer S (cola de animación)")]
    public float activeTailBufferSeconds = 0.09f;

    // ================= Air-stall (player) =================
    [Header("Air-stall (player)")]
    [Tooltip("Máximo de stalls del PLAYER durante una misma secuencia aérea (entre tocar suelos).")]
    public int maxAirStallsPerAirSequence = 3;
    // Duración viene de simpleAirStall.defaultStallMs

    // ===== Cooldown separado entre hits para Ctrl+S (vive en simpleAttack del S) =====
    float lastCtrlSHitTime = -999f;

    // ================= D durante/antes de HOLD =================
    [Header("D + HOLD")]
    public bool bufferDWhileHold = true;
    public float dWhileHoldBufferWindow = 0.18f;
    public bool bufferDPreHold = true;
    public float dPreHoldBufferWindow = 0.35f;

    // ----- Estado S / combo -----
    int sCount = 0;
    float lastSValidTime = -999f;

    // HOLD
    bool tryingToHold = false;
    float holdPressStartTime = -1f;

    // Buffer S
    bool hasBufferedS = false;
    float bufferedConsumeNotBefore = -999f;
    float bufferedExpireAt = -999f;

    // Secuencia aérea del PLAYER (para metadatos hacia el enemigo)
    int stallsThisAirSeq = 0;
    bool wasGroundedLastFrame = true;
    int myAirSeqId = 0; // aumenta cada vez que aterrizamos → próxima secuencia aérea

    // Buffer D en HOLD
    bool hasBufferedDWhileHold = false;
    float bufferedDWhileHoldExpireAt = -999f;

    // Buffer D pre-HOLD
    bool hasBufferedDPreHold = false;
    float bufferedDPreHoldExpireAt = -999f;

    void Reset() {
        controls = GetComponent<simpleControls>();
        if (airStall == null) airStall = GetComponent<simpleAirStall>();
    }

    void Awake() {
        if (airStall == null) airStall = GetComponent<simpleAirStall>();
    }

    void Update() {
        if (controls == null || attackSArea == null || attackDArea == null) return;

        bool grounded = (ground != null) && ground.IsGrounded();
        if (grounded && !wasGroundedLastFrame) {
            // ATERRIZÓ: reset player counters y avanza id de secuencia para próximos golpes aéreos
            stallsThisAirSeq = 0;
            myAirSeqId++;
            // No hay “stall enemigo armado” que limpiar, ya no lo manejamos acá
        }
        wasGroundedLastFrame = grounded;

        HandleSInput(grounded);
        HandleDInput(grounded);

        if (sCount > 0 && !attackSArea.InHold) {
            float dt = Time.time - lastSValidTime;
            bool guardingForHold = tryingToHold && controls.AttackSHeld();
            if (!guardingForHold && dt > comboWindowS) {
                ResetSCombo();
            }
        }

        TryConsumeSBufferIfReady();
        TryConsumeDPreHoldBufferIfReady();
        TryConsumeDWhileHoldBufferIfReady();

        if (attackSArea.InHold && !controls.AttackSHeld()) {
            attackSArea.StopHold();
        }
    }

    // ===================== S =====================

    bool CtrlSCooldownAllowsNow() {
        if (attackSArea == null) return true;
        if (!controls.StallHeld()) return true; // solo aplica con Ctrl activo
        float cd = Mathf.Max(0f, attackSArea.ctrlSBetweenHitsCooldown);
        if (cd <= 0f) return true;
        return (Time.time - lastCtrlSHitTime) >= cd;
    }

    void MarkCtrlSHitIfApplicable() {
        if (controls.StallHeld()) lastCtrlSHitTime = Time.time;
    }

    void AttachHitMetadataToSensor() {
        if (attackSArea.sensor == null) return;
        attackSArea.sensor.metaAttacker = this.transform;
        attackSArea.sensor.metaIsCtrlS = controls.StallHeld();
        attackSArea.sensor.metaAttackerAirSeqId = myAirSeqId;
    }

    void HandleSInput(bool groundedNow) {
        bool airborne = !groundedNow;

        if (controls.AttackSDown()) {
            // Player air-stall opcional con Ctrl (limitado por maxAirStallsPerAirSequence)
            if (airborne && controls.StallHeld() && stallsThisAirSeq < maxAirStallsPerAirSequence) {
                if (airStall != null) {
                    airStall.ApplyStall(); // usa default del componente
                    stallsThisAirSeq++;
                }
            }

            // (A) En IsActive: buffer cola
            if (attackSArea.IsActive) {
                float timeToActiveEnd = Mathf.Max(0f, attackSArea.ActiveUntil - Time.time);
                if (timeToActiveEnd <= activeTailBufferSeconds) {
                    hasBufferedS = true;
                    bufferedConsumeNotBefore = attackSArea.CooldownUntil;
                    bufferedExpireAt = attackSArea.CooldownUntil + bufferExpireAfterCd;
                }
                return;
            }

            // (B) En cooldown: buffer según modo
            if (attackSArea.InCooldown) {
                float timeToCdEnd = Mathf.Max(0f, attackSArea.CooldownUntil - Time.time);
                bool canOpen =
                    (sBufferMode == SBufferMode.FullCooldown) ||
                    (sBufferMode == SBufferMode.TailOnly && timeToCdEnd <= bufferTailSeconds);

                if (canOpen) {
                    hasBufferedS = true;
                    bufferedConsumeNotBefore = attackSArea.CooldownUntil;
                    bufferedExpireAt = attackSArea.CooldownUntil + bufferExpireAfterCd;
                }
                return;
            }

            // (C) Libre: gate de Ctrl+S
            if (!CtrlSCooldownAllowsNow()) return;

            // ANTES de disparar la ventana, adjuntamos metadatos
            AttachHitMetadataToSensor();

            // Disparo real
            if (attackSArea.FireOnce(AttackEffectKind.None)) {
                MarkCtrlSHitIfApplicable();
                lastSValidTime = Time.time;
                sCount = Mathf.Max(1, sCount + 1);

                // HOLD desde S3+
                if (sCount >= 3 && controls.AttackSHeld()) {
                    tryingToHold = true;
                    holdPressStartTime = Time.time;
                }
                else {
                    tryingToHold = false;
                    holdPressStartTime = -1f;
                }
            }
        }

        // HOLD si sostuvo suficiente
        if (tryingToHold && controls.AttackSHeld() &&
            (Time.time - holdPressStartTime) >= holdTimeS) {
            if (!attackSArea.IsActiveOrCooling && !attackSArea.InHold) {
                AttachHitMetadataToSensor(); // metadatos también para ticks HOLD si querés usar
                if (attackSArea.StartHold()) {
                    // no-op
                }
            }
            tryingToHold = false;
        }

        if (!controls.AttackSHeld() && tryingToHold) {
            tryingToHold = false;
            holdPressStartTime = -1f;
        }
    }

    void TryConsumeSBufferIfReady() {
        if (!hasBufferedS) return;

        if (Time.time >= bufferedConsumeNotBefore &&
            Time.time <= bufferedExpireAt &&
            !attackSArea.IsActiveOrCooling &&
            !attackSArea.InHold &&
            !attackDArea.IsActive) {

            // Gate Ctrl+S antes de consumir buffer
            if (!CtrlSCooldownAllowsNow()) { hasBufferedS = false; return; }

            hasBufferedS = false;

            AttachHitMetadataToSensor();

            if (attackSArea.FireOnce(AttackEffectKind.None)) {
                MarkCtrlSHitIfApplicable();
                lastSValidTime = Time.time;
                sCount = Mathf.Max(1, sCount + 1);

                if (sCount >= 3 && controls.AttackSHeld()) {
                    tryingToHold = true;
                    holdPressStartTime = Time.time;
                }
            }
        }

        if (Time.time > bufferedExpireAt) hasBufferedS = false;
    }

    void ResetSCombo() {
        sCount = 0;
        lastSValidTime = -999f;

        hasBufferedS = false;
        bufferedConsumeNotBefore = -999f;
        bufferedExpireAt = -999f;

        tryingToHold = false;
        holdPressStartTime = -1f;

        hasBufferedDWhileHold = false;
        bufferedDWhileHoldExpireAt = -999f;
        hasBufferedDPreHold = false;
        bufferedDPreHoldExpireAt = -999f;

        if (attackSArea.InHold) attackSArea.StopHold();
    }

    // ===================== D =====================

    void HandleDInput(bool groundedNow) {
        bool airborne = !groundedNow;

        if (controls.AttackDDown()) {
            if (airborne && controls.StallHeld() && stallsThisAirSeq < maxAirStallsPerAirSequence) {
                if (airStall != null) {
                    airStall.ApplyStall();
                    stallsThisAirSeq++;
                }
            }

            if (attackSArea.InHold) {
                if (bufferDWhileHold && (attackDArea.IsActiveOrCooling || !attackDArea.isActiveAndEnabled)) {
                    hasBufferedDWhileHold = true;
                    bufferedDWhileHoldExpireAt = Time.time + dWhileHoldBufferWindow;
                }
                else {
                    attackSArea.StopHold();
                    attackDArea.FireOnce(AttackEffectKind.Launch);
                    ResetSCombo();
                }
            }
            else {
                if (bufferDPreHold && tryingToHold) {
                    hasBufferedDPreHold = true;
                    bufferedDPreHoldExpireAt = Time.time + dPreHoldBufferWindow;
                    return;
                }

                if (!attackDArea.IsActiveOrCooling && !attackSArea.IsActive) {
                    attackDArea.FireOnce(AttackEffectKind.None);
                }
            }
        }
    }

    void TryConsumeDPreHoldBufferIfReady() {
        if (!hasBufferedDPreHold) return;

        if (Time.time > bufferedDPreHoldExpireAt) { hasBufferedDPreHold = false; return; }

        if (attackSArea.InHold) {
            hasBufferedDPreHold = false;
            attackSArea.StopHold();
            attackDArea.FireOnce(AttackEffectKind.Launch);
            ResetSCombo();
        }
    }

    void TryConsumeDWhileHoldBufferIfReady() {
        if (!hasBufferedDWhileHold) return;

        if (!attackSArea.InHold || Time.time > bufferedDWhileHoldExpireAt) { hasBufferedDWhileHold = false; return; }

        if (!attackDArea.IsActiveOrCooling && attackDArea.isActiveAndEnabled) {
            hasBufferedDWhileHold = false;
            attackSArea.StopHold();
            attackDArea.FireOnce(AttackEffectKind.Launch);
            ResetSCombo();
        }
    }
}
