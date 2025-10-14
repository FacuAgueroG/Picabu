using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class simpleGravity2D : MonoBehaviour {
    public simpleControls controls;    // Para leer JumpHeld()
    Rigidbody2D rb;

    [Header("Gravedades")]
    [Tooltip("Gravedad mientras SUBE (más chico = sube más fácil)")]
    public float upGravityScale = 1.0f;

    [Tooltip("Gravedad base al comenzar la CAÍDA")]
    public float downGravityScaleStart = 2.0f;

    [Tooltip("Gravedad tope al final de la rampa de CAÍDA")]
    public float downGravityScaleMax = 4.0f;

    [Header("Rampa de caída (sensación de peso)")]
    [Tooltip("Tiempo que tarda en pasar de Start a Max (segundos)")]
    public float fallRampTime = 0.20f;   // 200 ms suele sentirse bien

    [Header("Corte de salto al soltar (en SUBIDA)")]
    public bool hardCutOnRelease = true;
    [Tooltip("Tras soltar, límite superior para la Y (0 para cortar al instante)")]
    public float cutUpwardSpeed = 0f;
    [Tooltip("Empujón hacia abajo al soltar (0 = desactivado)")]
    public float fallKickSpeed = 0f;
    [Tooltip("Si NO usás hard cut, gravedad multiplicada al soltar")]
    public float jumpCutMultiplier = 2.0f;

    [Header("Límites")]
    [Tooltip("Velocidad terminal de caída (en unidades/seg)")]
    public float maxFallSpeed = 25f;

    // Estado
    bool suppressCutUntilFall = false;   // usado por el doble salto
    float fallTimer = 0f;                // tiempo acumulado cayendo
    float prevVy = 0f;                   // para detectar cambio de signo

    public void SuppressCutUntilFall() => suppressCutUntilFall = true;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate() {
        Vector2 v = rb.linearVelocity;

        // Detectar inicio de caída (ápice): de v.y >= 0 a v.y < 0
        bool startedFallingThisFrame = (prevVy >= 0f && v.y < 0f);
        if (startedFallingThisFrame) {
            fallTimer = 0f;          // reiniciar rampa al empezar a caer
            suppressCutUntilFall = false;
        }

        if (v.y > 0f) // SUBIENDO
        {
            fallTimer = 0f; // mientras sube, no acumulamos rampa

            if (!suppressCutUntilFall && controls != null && !controls.JumpHeld()) {
                if (hardCutOnRelease) {
                    if (v.y > cutUpwardSpeed) v.y = cutUpwardSpeed;
                    if (fallKickSpeed > 0f) v.y = -Mathf.Abs(fallKickSpeed);
                    rb.linearVelocity = v;

                    // Conviene ya usar la gravedad de caída (arranca firme)
                    rb.gravityScale = downGravityScaleStart;
                }
                else {
                    // Corte suave
                    rb.gravityScale = upGravityScale * jumpCutMultiplier;
                }
            }
            else {
                // Manteniendo botón o suprimido por doble salto
                rb.gravityScale = upGravityScale;
            }
        }
        else if (v.y < 0f) // CAYENDO
        {
            // Progresión de gravedad: de Start → Max en fallRampTime
            if (fallRampTime <= 0f) {
                rb.gravityScale = downGravityScaleMax;
            }
            else {
                fallTimer += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(fallTimer / fallRampTime);
                // Ease-in muy simple (t^2) para que se note el “peso” sin ser brusco
                float eased = t * t;
                rb.gravityScale = Mathf.Lerp(downGravityScaleStart, downGravityScaleMax, eased);
            }

            // Cap de velocidad de caída
            if (v.y < -maxFallSpeed) {
                v.y = -maxFallSpeed;
                rb.linearVelocity = v;
            }
        }
        else // v.y ≈ 0 (ápice)
        {
            // Usemos la gravedad de subida como base mientras “flota” en 0
            rb.gravityScale = upGravityScale;
        }

        prevVy = v.y;
    }
}
