using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class simpleJump : MonoBehaviour {
    public simpleControls controls;
    public simpleGround2D ground;
    public simpleGravity2D gravityHelper; // recomendado

    Rigidbody2D rb;

    [Header("Modo opcional: calcular por Altura + Tiempo al ápice")]
    public bool useHeightAndTime = false;
    [Min(0.01f)] public float apexHeight = 3.0f;   // H del primer salto
    [Min(0.01f)] public float timeToApex = 0.35f;  // T al ápice (subida)

    [Header("Saltos (modo manual o resultado del cálculo)")]
    public float firstJumpSpeed = 12f;
    public float doubleJumpSpeed = 10f;

    [Header("Doble salto por altura (opcional, con el modo activado)")]
    public bool useDoubleFromHeight = true;
    public float doubleJumpHeight = 2.2f;

    [Header("Aéreos")]
    public int maxAirJumps = 1;

    int airJumpsUsed = 0;
    bool wasGroundedLastFrame = false;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        ApplyHeightTimeIfNeeded();
    }

    void OnValidate() {
        ApplyHeightTimeIfNeeded();
    }

    void ApplyHeightTimeIfNeeded() {
        if (!useHeightAndTime || gravityHelper == null) return;

        float gWorld = Mathf.Abs(Physics2D.gravity.y);
        float gUp = (2f * apexHeight) / (timeToApex * timeToApex);
        gravityHelper.upGravityScale = gUp / gWorld;
        firstJumpSpeed = gUp * timeToApex;

        if (useDoubleFromHeight) {
            doubleJumpSpeed = Mathf.Sqrt(2f * gUp * Mathf.Max(0f, doubleJumpHeight));
        }
    }

    void Update() {
        bool grounded = (ground != null) && ground.IsGrounded();
        if (grounded && !wasGroundedLastFrame) {
            airJumpsUsed = 0;
        }
        wasGroundedLastFrame = grounded;

        if (controls != null && controls.JumpDown()) {
            if (grounded) {
                var v = rb.linearVelocity;
                v.y = firstJumpSpeed;
                rb.linearVelocity = v;
            }
            else if (airJumpsUsed < maxAirJumps) {
                var v = rb.linearVelocity;
                v.y = doubleJumpSpeed;
                rb.linearVelocity = v;
                airJumpsUsed++;

                if (gravityHelper != null)
                    gravityHelper.SuppressCutUntilFall();
            }
        }
    }

    public void ConsumeGroundJumpToken() {
        if (ground != null && ground.IsGrounded())
            airJumpsUsed = Mathf.Min(maxAirJumps, airJumpsUsed + 1);
    }
}
