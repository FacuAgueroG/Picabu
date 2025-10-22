using UnityEngine;

public class simpleCombatController : MonoBehaviour {
    [Header("Referencias")]
    public simpleControls controls;
    public simpleAttack attackSArea;
    public simpleAttack attackDArea;
    public simpleGround2D ground;
    public simpleAirStall airStall;

    [Header("Combo S (base)")]
    [Tooltip("Tiempo máx entre S válidas para no resetear el combo.")]
    public float comboWindowS = 0.50f;
    [Tooltip("Tiempo que debes mantener S para entrar a HOLD (solo S3+).")]
    public float holdTimeS = 0.25f;

    // ================= Buffer S (configurable) =================
    public enum SBufferMode { FullCooldown, TailOnly }

    [Header("Buffer S (en cooldown)")]
    [Tooltip("Modo de apertura del buffer mientras S está en cooldown.")]
    public SBufferMode sBufferMode = SBufferMode.FullCooldown;

    [Tooltip("Si el modo es TailOnly, el buffer se abre solo en los últimos Xs del CD.")]
    public float bufferTailSeconds = 0.12f;      // 120 ms

    [Tooltip("Validez del buffer una vez termina el CD.")]
    public float bufferExpireAfterCd = 0.12f;    // 120 ms

    [Header("Buffer S (cola de animación)")]
    [Tooltip("Durante IsActive, si faltan <= Xs para terminar, se permite buffer al CD siguiente.")]
    public float activeTailBufferSeconds = 0.09f; // 90 ms (punto de partida)

    // ================= Air-stall con modificador =================
    [Header("Air-stall (con Left Ctrl)")]
    [Tooltip("Duración del stall vertical en ms (cuando se sostiene Left Ctrl en el aire).")]
    public float stallMs = 110f;
    [Tooltip("Máximo de stalls durante una misma secuencia aérea (entre tocar suelos).")]
    public int maxAirStallsPerAirSequence = 3;

    // ================= D durante/antes de HOLD (robustez) =================
    [Header("D + HOLD")]
    [Tooltip("Si D se pulsa en HOLD y D no está libre, se bufferiza sin cortar el HOLD.")]
    public bool bufferDWhileHold = true;
    [Tooltip("Ventana de espera para disparar D cuando quede libre (si se pulsó en HOLD).")]
    public float dWhileHoldBufferWindow = 0.18f; // 180 ms

    [Tooltip("Si D se pulsa MIENTRAS se está 'preparando' el HOLD (tryingToHold), se bufferiza y se consume al entrar en HOLD.")]
    public bool bufferDPreHold = true;
    [Tooltip("Ventana máx para que ese D buffered se consuma al entrar en HOLD.")]
    public float dPreHoldBufferWindow = 0.35f; // >= holdTimeS + margen

    // ====== Detección espejo D ======
    [Header("Combo D (espejo)")]
    public float comboWindowD = 0.50f;
    public float holdTimeD = 0.25f;

    // ----- Estado S / combo -----
    int sCount = 0;
    float lastSValidTime = -999f;

    // HOLD S
    bool tryingToHoldS = false;
    float holdSPressStartTime = -1f;

    // Buffer S
    bool hasBufferedS = false;
    float bufferedConsumeNotBefore = -999f; // no antes del fin de CD
    float bufferedExpireAt = -999f;

    // Secuencia aérea (para limitar stalls)
    int stallsThisAirSeq = 0;
    bool wasGroundedLastFrame = true;

    // Buffer D mientras estamos en HOLD S
    bool hasBufferedDWhileHold = false;
    float bufferedDWhileHoldExpireAt = -999f;

    // Buffer D cuando todavía NO estamos en HOLD pero lo estamos preparando (tryingToHold)
    bool hasBufferedDPreHold = false;
    float bufferedDPreHoldExpireAt = -999f;

    // ====== Estado D espejo ======
    int dCount = 0;
    float lastDValidTime = -999f;

    bool tryingToHoldD = false;
    float holdDPressStartTime = -1f;

    void Reset() {
        controls = GetComponent<simpleControls>();
        if (airStall == null) airStall = GetComponent<simpleAirStall>();
    }

    void Awake() {
        if (airStall == null) airStall = GetComponent<simpleAirStall>();
    }

    void Update() {
        if (controls == null || attackSArea == null || attackDArea == null) return;

        // Track de aire/suelo para resetear contador de stalls
        bool grounded = (ground != null) && ground.IsGrounded();
        if (grounded && !wasGroundedLastFrame) {
            stallsThisAirSeq = 0;
        }
        wasGroundedLastFrame = grounded;

        HandleSInput(grounded);
        HandleDInput(grounded);

        // Ventana de combo: S
        if (sCount > 0 && !attackSArea.InHold) {
            float dt = Time.time - lastSValidTime;
            bool guardingForHold = tryingToHoldS && controls.AttackSHeld();
            if (!guardingForHold && dt > comboWindowS) {
                ResetSCombo();
            }
        }

        // Ventana de combo: D
        if (dCount > 0 && !attackDArea.InHold) {
            float dt = Time.time - lastDValidTime;
            bool guardingForHold = tryingToHoldD && controls.AttackDHeld();
            if (!guardingForHold && dt > comboWindowD) {
                ResetDCombo();
            }
        }

        TryConsumeSBufferIfReady();
        TryConsumeDPreHoldBufferIfReady();
        TryConsumeDWhileHoldBufferIfReady();

        // HOLD robusto: cancelar si deja de sostener
        if (attackSArea.InHold && !controls.AttackSHeld()) {
            attackSArea.StopHold();
        }
        if (attackDArea.InHold && !controls.AttackDHeld()) {
            attackDArea.StopHold();
        }
    }

    // ===================== S =====================

    void HandleSInput(bool groundedNow) {
        bool airborne = !groundedNow;

        // --- Down ---
        if (controls.AttackSDown()) {
            // AIR STALL con modificador
            if (airborne && controls.StallHeld() && stallsThisAirSeq < maxAirStallsPerAirSequence) {
                if (airStall != null) {
                    airStall.ApplyStall(stallMs);
                    stallsThisAirSeq++;
                }
            }

            // (A) Durante IsActive: permitir buffer SOLO en la cola de la animación
            if (attackSArea.IsActive) {
                float timeToActiveEnd = Mathf.Max(0f, attackSArea.ActiveUntil - Time.time);
                if (timeToActiveEnd <= activeTailBufferSeconds) {
                    hasBufferedS = true;
                    bufferedConsumeNotBefore = attackSArea.CooldownUntil;
                    bufferedExpireAt = attackSArea.CooldownUntil + bufferExpireAfterCd;
                }
                return;
            }

            // (B) Si está en cooldown: buffer según modo
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

            // (C) Libre: dispara ya
            if (attackSArea.FireOnce(AttackEffectKind.None)) {
                lastSValidTime = Time.time;
                sCount = Mathf.Max(1, sCount + 1);

                // Intento HOLD desde S3+
                if (sCount >= 3 && controls.AttackSHeld()) {
                    tryingToHoldS = true;
                    holdSPressStartTime = Time.time;
                }
                else {
                    tryingToHoldS = false;
                    holdSPressStartTime = -1f;
                }
            }
        }

        // --- Held: HOLD desde S3+ (si sostiene lo suficiente) ---
        if (tryingToHoldS && controls.AttackSHeld() &&
            (Time.time - holdSPressStartTime) >= holdTimeS) {
            if (!attackSArea.IsActiveOrCooling && !attackSArea.InHold) {
                if (attackSArea.StartHold()) {
                    // Si había D buffered "pre-hold", se consumirá al entrar al HOLD
                }
            }
            tryingToHoldS = false; // consumimos el intento
        }

        // Abortamos intento de HOLD si soltó antes del tiempo
        if (!controls.AttackSHeld() && tryingToHoldS) {
            tryingToHoldS = false;
            holdSPressStartTime = -1f;
        }
    }

    void TryConsumeSBufferIfReady() {
        if (!hasBufferedS) return;

        if (Time.time >= bufferedConsumeNotBefore &&
            Time.time <= bufferedExpireAt &&
            !attackSArea.IsActiveOrCooling &&
            !attackSArea.InHold &&
            !attackDArea.IsActive) {
            hasBufferedS = false;
            if (attackSArea.FireOnce(AttackEffectKind.None)) {
                lastSValidTime = Time.time;
                sCount = Mathf.Max(1, sCount + 1);

                if (sCount >= 3 && controls.AttackSHeld()) {
                    tryingToHoldS = true;
                    holdSPressStartTime = Time.time;
                }
            }
        }

        if (Time.time > bufferedExpireAt) {
            hasBufferedS = false;
        }
    }

    void ResetSCombo() {
        sCount = 0;
        lastSValidTime = -999f;

        hasBufferedS = false;
        bufferedConsumeNotBefore = -999f;
        bufferedExpireAt = -999f;

        tryingToHoldS = false;
        holdSPressStartTime = -1f;

        hasBufferedDWhileHold = false;
        bufferedDWhileHoldExpireAt = -999f;
        hasBufferedDPreHold = false;
        bufferedDPreHoldExpireAt = -999f;

        if (attackSArea.InHold) attackSArea.StopHold();
    }

    // ===================== D (con espejo y finalizador) =====================

    void HandleDInput(bool groundedNow) {
        bool airborne = !groundedNow;

        if (controls.AttackDDown()) {
            // AIR STALL opcional con D
            if (airborne && controls.StallHeld() && stallsThisAirSeq < maxAirStallsPerAirSequence) {
                if (airStall != null) {
                    airStall.ApplyStall(stallMs);
                    stallsThisAirSeq++;
                }
            }

            // PRIORIDAD: D sobre HOLD S ⇒ cancelación + Launch (SSS(HOLD)+D) — SIEMPRE
            if (attackSArea.InHold) {
                attackSArea.StopHold();
                attackDArea.ForceFireOnce(AttackEffectKind.Launch); // <<<<<< forzado
                ResetSCombo();

                // Contar D para su combo espejo
                lastDValidTime = Time.time;
                dCount = Mathf.Max(1, dCount + 1);
                return;
            }

            // D normal (espejo de S)
            if (attackDArea.IsActiveOrCooling) {
                // (opcional: buffer espejo)
                return;
            }

            if (attackDArea.FireOnce(AttackEffectKind.None)) {
                lastDValidTime = Time.time;
                dCount = Mathf.Max(1, dCount + 1);

                // Intento HOLD desde D3+
                if (dCount >= 3 && controls.AttackDHeld()) {
                    tryingToHoldD = true;
                    holdDPressStartTime = Time.time;
                }
                else {
                    tryingToHoldD = false;
                    holdDPressStartTime = -1f;
                }
            }
        }

        // Entrar a HOLD D3+
        if (tryingToHoldD && controls.AttackDHeld() &&
            (Time.time - holdDPressStartTime) >= holdTimeD) {
            if (!attackDArea.IsActiveOrCooling && !attackDArea.InHold) {
                attackDArea.StartHold();
            }
            tryingToHoldD = false;
        }

        if (!controls.AttackDHeld() && tryingToHoldD) {
            tryingToHoldD = false;
            holdDPressStartTime = -1f;
        }

        // FINALIZADOR: DDD(Hold)+S (player debe estar en aire) — SIEMPRE
        if (attackDArea.InHold && controls.AttackSDown()) {
            if (!groundedNow) {
                attackDArea.StopHold();
                attackSArea.ForceFireOnce(AttackEffectKind.DragToGround); // <<<<<< forzado
                ResetDCombo();

                lastSValidTime = Time.time;
                sCount = Mathf.Max(1, sCount + 1);
                return;
            }
            else {
                // Si está en suelo, no hay finalización (dispara S normal para no dejar muerta la entrada)
                attackDArea.StopHold();
                attackSArea.FireOnce(AttackEffectKind.None);
                ResetDCombo();

                lastSValidTime = Time.time;
                sCount = Mathf.Max(1, sCount + 1);
                return;
            }
        }
    }

    void TryConsumeDPreHoldBufferIfReady() {
        if (!hasBufferedDPreHold) return;

        if (Time.time > bufferedDPreHoldExpireAt) {
            hasBufferedDPreHold = false;
            return;
        }

        if (attackSArea.InHold) {
            hasBufferedDPreHold = false;
            attackSArea.StopHold();
            attackDArea.ForceFireOnce(AttackEffectKind.Launch); // <<<<<< forzado
            ResetSCombo();
        }
    }

    void TryConsumeDWhileHoldBufferIfReady() {
        if (!hasBufferedDWhileHold) return;

        if (!attackSArea.InHold || Time.time > bufferedDWhileHoldExpireAt) {
            hasBufferedDWhileHold = false;
            return;
        }

        if (!attackDArea.IsActiveOrCooling && attackDArea.isActiveAndEnabled) {
            hasBufferedDWhileHold = false;
            attackSArea.StopHold();
            attackDArea.ForceFireOnce(AttackEffectKind.Launch); // <<<<<< forzado
            ResetSCombo();
        }
    }

    void ResetDCombo() {
        dCount = 0;
        lastDValidTime = -999f;
        tryingToHoldD = false;
        holdDPressStartTime = -1f;
        if (attackDArea.InHold) attackDArea.StopHold();
    }
}
