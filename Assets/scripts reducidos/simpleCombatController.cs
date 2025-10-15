using UnityEngine;

public class simpleCombatController : MonoBehaviour {
    [Header("Referencias")]
    public simpleControls controls;
    [Tooltip("Área S (circular): simpleAttack + simpleAttackSensor + collider + sprite")]
    public simpleAttack attackSArea;
    [Tooltip("Área D (rectángulo fino): simpleAttack + simpleAttackSensor + collider + sprite")]
    public simpleAttack attackDArea;

    [Header("Combo S")]
    [Tooltip("Tiempo máx entre S válidas para no resetear el combo.")]
    public float comboWindowS = 0.50f;
    [Tooltip("Tiempo que debes mantener S para entrar a HOLD (solo si ya estás en S3+).")]
    public float holdTimeS = 0.25f;

    [Header("Permisividad (S)")]
    [Tooltip("Ventana de buffer para S (si SDown ocurre mientras S está ocupado/en CD).")]
    public float inputBufferS = 0.14f;            // 140ms: captura SDown temprano durante CD/ocupado
    [Tooltip("Gracia de ‘tarde’ para mantener combo aunque la ventana haya expirado por poco.")]
    public float lateGraceS = 0.10f;              // 100ms: acepta un SDown apenas tarde sin romper combo
    [Tooltip("Bloqueo corto tras consumir el buffer para evitar doble disparo/race.")]
    public float postConsumeLockoutS = 0.05f;     // 50ms

    // Estado interno combo/hold S
    int sCount = 0;                 // 0=base; 1=S1; 2=S2; 3+=S3...
    float lastSValidTime = -999f;   // tiempo última S válida (que encendió ventana)
    bool tryingToHold = false;      // midiendo “mantener S”
    float holdPressStartTime = -1f;

    // Estado del buffer S
    bool hasBufferedS = false;
    float bufferedSExpiresAt = -999f;   // Time.time en que el buffer deja de ser válido
    float lastSDownTime = -999f;        // para late grace (marca el último Down real del usuario)
    float postConsumeLockoutUntil = -999f;

    void Reset() {
        controls = GetComponent<simpleControls>();
    }

    void Update() {
        if (controls == null || attackSArea == null || attackDArea == null) return;

        // 1) Expiración de combo por ventana (con "late grace" aplicada al siguiente Down)
        if (!attackSArea.InHold && sCount > 0) {
            float dt = Time.time - lastSValidTime;
            if (dt > comboWindowS + lateGraceS) {
                // Ventana + gracia definitivamente expiradas.
                ResetSCombo();
            }
            // Nota: si dt está entre (comboWindowS, comboWindowS + lateGraceS],
            // NO reseteamos aún; decidimos al procesar el próximo SDown real.
        }

        HandleDInput();
        HandleSInputAndHold();   // procesa SDown reales y HOLD
        TryConsumeSBuffer();     // si S quedó en buffer y ahora está libre, lo consume como KeyDown virtual
    }

    // ===================== S (permisivo) =====================

    void HandleSInputAndHold() {
        bool sBusy = attackSArea.IsActiveOrCooling; // ocupado o en cooldown
        bool sInHold = attackSArea.InHold;

        // --- SDown real del usuario ---
        if (controls.AttackSDown()) {
            lastSDownTime = Time.time;

            // Si NO estamos en HOLD y S no está disponible ahora, bufferizar
            if (!sInHold && (sBusy || attackDArea.IsActive)) {
                BufferS();
            }
            else {
                // S disponible (no ocupado, no en HOLD, D no activo)
                ProcessSDownWithLateGrace();
            }
        }

        // --- Construcción de HOLD (se mide SIEMPRE desde el momento en que S disparó disponible) ---
        // Requisito: estar en S3+ y NO estar en HOLD
        if (!sInHold && sCount >= 3 && tryingToHold && controls.AttackSHeld()) {
            // Si ya cumplimos tiempo de hold y S no está ocupado, entramos a HOLD
            if ((Time.time - holdPressStartTime) >= holdTimeS && !attackSArea.IsActiveOrCooling) {
                bool ok = attackSArea.StartHold();
                if (ok) {
                    // Quedamos en HOLD esperando D o que suelte S
                }
                tryingToHold = false; // dejamos de medir
            }
        }

        // Salir de HOLD al soltar S
        if (attackSArea.InHold && controls.AttackSUp()) {
            attackSArea.StopHold();
            ResetSCombo();
        }
    }

    // Procesa un SDown en el que S está disponible, aplicando "late grace" si corresponde
    void ProcessSDownWithLateGrace() {
        // Determinar si debemos resetear el combo por expiración SIN gracia
        if (sCount > 0) {
            float dt = Time.time - lastSValidTime;

            if (dt > comboWindowS) {
                // Se pasó la ventana normal: ¿entra en la gracia tarde?
                if (dt <= comboWindowS + lateGraceS) {
                    // Aceptamos este Down como parte del mismo combo (no reseteamos).
                }
                else {
                    // Ventana + gracia expiradas → reset
                    ResetSCombo();
                }
            }
        }

        // Disparar S una vez
        bool fired = attackSArea.FireOnce(AttackEffectKind.None);
        if (fired) {
            sCount = Mathf.Min(sCount + 1, 999);
            lastSValidTime = Time.time;

            // Si alcanzamos S3+ y el jugador mantiene S, empezamos a medir hold DESDE AHORA
            if (sCount >= 3 && controls.AttackSHeld()) {
                tryingToHold = true;
                holdPressStartTime = Time.time; // <<< importante: medimos desde el disparo, no antes
            }
            else {
                tryingToHold = false;
                holdPressStartTime = -1f;
            }
        }
    }

    void BufferS() {
        hasBufferedS = true;
        bufferedSExpiresAt = Time.time + inputBufferS;
        // No armamos hold aquí; el hold se armará (si corresponde) al CONSUMIR el buffer.
    }

    void TryConsumeSBuffer() {
        if (!hasBufferedS) return;

        // Requisitos para consumir: S libre (no ocupado ni en HOLD), D no activo, buffer vigente y sin lockout
        bool sBusy = attackSArea.IsActiveOrCooling;
        bool sInHold = attackSArea.InHold;
        if (sBusy || sInHold || attackDArea.IsActive) return;

        if (Time.time > bufferedSExpiresAt) {
            // Venció el buffer → limpiar
            hasBufferedS = false;
            return;
        }

        if (Time.time < postConsumeLockoutUntil) {
            // Estamos en pequeño lockout post-consumo
            return;
        }

        // Consumir como "keydown virtual" en este frame
        hasBufferedS = false;

        // Aplicar late grace en el consumo también (misma lógica que ProcessSDownWithLateGrace)
        if (sCount > 0) {
            float dt = Time.time - lastSValidTime;
            if (dt > comboWindowS) {
                if (dt <= comboWindowS + lateGraceS) {
                    // Aceptamos dentro de la gracia
                }
                else {
                    ResetSCombo();
                }
            }
        }

        bool fired = attackSArea.FireOnce(AttackEffectKind.None);
        if (fired) {
            sCount = Mathf.Min(sCount + 1, 999);
            lastSValidTime = Time.time;

            // Regla acordada: si el botón sigue físicamente presionado, empezamos a medir HOLD DESDE EL CONSUMO
            if (sCount >= 3 && controls.AttackSHeld()) {
                tryingToHold = true;
                holdPressStartTime = Time.time; // arranca medición ahora
            }
            else {
                tryingToHold = false;
                holdPressStartTime = -1f;
            }
        }

        // Lockout corto para evitar dobles disparos por cambios de estado en el mismo frame
        postConsumeLockoutUntil = Time.time + postConsumeLockoutS;
    }

    // ===================== D (sin cambios funcionales) =====================

    void HandleDInput() {
        if (controls.AttackDDown()) {
            if (attackSArea.InHold) {
                // EXCEPCIÓN: D durante S(HOLD) → apaga S y dispara D UNA vez con Launch
                attackSArea.StopHold();
                attackDArea.FireOnce(AttackEffectKind.Launch); // si D está en CD, simplemente no sale
                ResetSCombo();

                // Nota: no bufferizamos S mientras estamos en HOLD.
                hasBufferedS = false;
            }
            else {
                // D suelto: bloqueado si S está activo ahora mismo
                if (!attackDArea.IsActiveOrCooling && !attackSArea.IsActive) {
                    attackDArea.FireOnce(AttackEffectKind.None);
                    // Cambiar de botón antes de S3 cancela S
                    if (sCount < 3) ResetSCombo();
                }
            }
        }
    }

    // ===================== Utils =====================

    void ResetSCombo() {
        sCount = 0;
        lastSValidTime = -999f;
        tryingToHold = false;
        holdPressStartTime = -1f;

        // Limpiar estados de buffer asociados
        hasBufferedS = false;
        bufferedSExpiresAt = -999f;
        postConsumeLockoutUntil = -999f;

        if (attackSArea.InHold) attackSArea.StopHold();
    }
}
