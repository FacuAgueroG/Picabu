using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class simpleJump2D : MonoBehaviour {
    public simpleControls controls;
    public simpleGround2D ground;
    public simpleGravity2D gravityHelper; // recomendado

    Rigidbody2D rb;

    [Header("Modo opcional: calcular por Altura + Tiempo al ápice")]
    public bool useHeightAndTime = false;
    [Min(0.01f)] public float apexHeight = 3.0f;   // H del primer salto
    [Min(0.01f)] public float timeToApex = 0.35f;  // T al ápice (subida)

    [Header("Saltos (modo manual o resultado del cálculo)")]
    [Tooltip("Velocidad vertical inicial del PRIMER salto (si usás el modo, se calcula)")]
    public float firstJumpSpeed = 12f;

    [Tooltip("Velocidad vertical del DOBLE salto (si usás el modo y 'useDoubleFromHeight', se calcula)")]
    public float doubleJumpSpeed = 10f;

    [Header("Doble salto por altura (opcional, con el modo activado)")]
    public bool useDoubleFromHeight = true;
    [Min(0f)] public float doubleJumpHeight = 2.2f;

    [Header("Aéreos")]
    [Tooltip("Cuántos saltos en el aire (1 = doble salto). En suelo siempre podés iniciar el primero.")]
    public int maxAirJumps = 1;

    // Estado interno
    int airJumpsUsed = 0;
    bool wasGroundedLastFrame = false;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        ApplyHeightTimeIfNeeded();
    }

    void OnValidate() {
        // Para que al tocar sliders veas el efecto sin entrar al Play (si el helper está asignado en el inspector)
        ApplyHeightTimeIfNeeded();
    }

    void ApplyHeightTimeIfNeeded() {
        if (!useHeightAndTime || gravityHelper == null) return;

        float gWorld = Mathf.Abs(Physics2D.gravity.y); // ~9.81 por defecto
        float gUp = (2f * apexHeight) / (timeToApex * timeToApex); // g_up deseada

        // Seteamos la gravedad de SUBIDA en el helper
        gravityHelper.upGravityScale = gUp / gWorld;

        // La velocidad inicial que logra ese T al ápice
        firstJumpSpeed = gUp * timeToApex;

        // Doble salto por altura fija (usa la MISMA g_up para la subida)
        if (useDoubleFromHeight) {
            doubleJumpSpeed = Mathf.Sqrt(2f * gUp * Mathf.Max(0f, doubleJumpHeight));
        }
        // Ojo: downGravityScale/jumpCutMultiplier/maxFallSpeed se siguen ajustando en simpleGravity2D
    }

    void Update() {
        bool grounded = (ground != null) && ground.IsGrounded();

        // Reset al tocar suelo
        if (grounded && !wasGroundedLastFrame) {
            airJumpsUsed = 0;
        }
        wasGroundedLastFrame = grounded;

        // Saltar al presionar (sin buffer/coyote)
        if (controls != null && controls.JumpDown()) {
            if (grounded) {
                // Primer salto (variable por hold → lo maneja simpleGravity2D con JumpHeld)
                var v = rb.linearVelocity;
                v.y = firstJumpSpeed;
                rb.linearVelocity = v;
            }
            else if (airJumpsUsed < maxAirJumps) {
                // Doble salto (altura fija, ignora hold):
                var v = rb.linearVelocity;
                v.y = doubleJumpSpeed;
                rb.linearVelocity = v;
                airJumpsUsed++;

                // Desactivar el 'jump-cut' hasta que empiece la caída,
                // así el doble salto NO se acorta si soltás el botón.
                if (gravityHelper != null)
                    gravityHelper.SuppressCutUntilFall();
            }
        }
    }

    // Dentro de tu clase simpleJump2D:
    public void ConsumeGroundJumpToken() {
        // Si usás contador de aéreos:
        // al simular un salto desde suelo, marcamos que ya "gastaste" el de suelo.
        // Ajustalo a tu lógica si usás otra variable.
        // Ejemplo simple:
        if (ground != null && ground.IsGrounded()) airJumpsUsed = Mathf.Min(maxAirJumps, airJumpsUsed + 1);
    }

}
