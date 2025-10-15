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

    // Estado interno combo/hold S
    int sCount = 0;                 // 0=base; 1=S1; 2=S2; 3+=S3...
    float lastSValidTime = -999f;   // tiempo última S válida (que encendió ventana)
    bool tryingToHold = false;      // medimos “mantener S”
    float holdPressStartTime = -1f;

    void Reset() {
        controls = GetComponent<simpleControls>();
    }

    void Update() {
        if (controls == null || attackSArea == null || attackDArea == null) return;

        // Expiración de combo por ventana
        if (sCount > 0 && (Time.time - lastSValidTime) > comboWindowS && !attackSArea.InHold) {
            ResetSCombo();
        }

        HandleDInput();
        HandleSInputAndHold();
    }

    void HandleSInputAndHold() {
        bool sBusy = attackSArea.IsActiveOrCooling;

        // SDown: sólo si S no está ocupado y D no está activo ahora (bloqueo de prioridad)
        if (controls.AttackSDown() && !sBusy && !attackDArea.IsActive) {
            // Si la ventana de combo expiró, resetea antes de contar
            if (sCount > 0 && (Time.time - lastSValidTime) > comboWindowS) {
                ResetSCombo();
            }

            bool fired = attackSArea.FireOnce(AttackEffectKind.None);
            if (fired) {
                sCount = Mathf.Min(sCount + 1, 999);
                lastSValidTime = Time.time;

                // Si alcanzamos S3+ y el jugador mantiene S, empezamos a medir hold
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

        // Entrar a HOLD (solo S3+, no estar ya en HOLD, S mantenida)
        if (!attackSArea.InHold && sCount >= 3 && tryingToHold && controls.AttackSHeld()) {
            if ((Time.time - holdPressStartTime) >= holdTimeS && !attackSArea.IsActiveOrCooling) {
                bool ok = attackSArea.StartHold();
                if (ok) {
                    // quedamos en HOLD esperando D o que suelte S
                }
                tryingToHold = false;
            }
        }

        // Salir de HOLD al soltar S
        if (attackSArea.InHold && controls.AttackSUp()) {
            attackSArea.StopHold();
            ResetSCombo();
        }
    }

    void HandleDInput() {
        if (controls.AttackDDown()) {
            if (attackSArea.InHold) {
                // EXCEPCIÓN: D durante S(HOLD) → apaga S y dispara D UNA vez con Launch
                attackSArea.StopHold();
                attackDArea.FireOnce(AttackEffectKind.Launch); // si D está en CD, simplemente no sale (regla tuya)
                ResetSCombo();
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

    void ResetSCombo() {
        sCount = 0;
        lastSValidTime = -999f;
        tryingToHold = false;
        holdPressStartTime = -1f;

        if (attackSArea.InHold) attackSArea.StopHold();
    }
}
