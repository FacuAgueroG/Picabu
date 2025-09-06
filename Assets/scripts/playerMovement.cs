using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
public class Player2DController : MonoBehaviour {
    [Header("Movement")]
    [SerializeField] private float baseSpeed = 6f;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float accel = 999f; // “Sin inercia”: usa MoveTowards muy alto
    [SerializeField] private bool faceRightDefault = true;

    [Header("Mario-style Jump")]
    [SerializeField] private float fallGravityMultiplier = 2.5f; // caer más rápido
    [SerializeField] private float lowJumpMultiplier = 2.0f;     // cortar salto al soltar tecla

    [Header("Jump (Charge)")]
    [SerializeField] private float jumpMinForce = 6f;
    [SerializeField] private float jumpMaxForce = 14f;
    [SerializeField] private float jumpMaxChargeTime = 0.35f; // tiempo para cargar al máximo
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;

    [Header("Ground Check (Raycast)")]
    [SerializeField] private Vector2 groundRayOffset = new Vector2(0f, -0.5f);
    [SerializeField] private float groundRayLength = 0.2f;
    [SerializeField] private LayerMask groundMask; // además del Tag "Ground"
    [SerializeField] private string groundTag = "Ground";

    [Header("Dash (8 Direcciones)")]
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashDistance = 6f;
    [SerializeField] private int dashMaxCharges = 2;
    [SerializeField] private float dashCooldown = 1.25f; // cuando se gastan ambas cargas

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private Rigidbody2D rb;
    private float currentVelX;
    private int moveDir = 0;                // -1, 0, 1
    private int lastPressedDir = 0;         // prioridad a la última tecla presionada
    private bool facingRight;

    // Jump charge
    private bool isChargingJump = false;
    private float jumpChargeTimer = 0f;
    private bool isGrounded = false;
    private bool canDoubleJump = false;

    // Dash
    private bool isDashing = false;
    private int dashCharges;
    private float dashRegenTimer = 0f;
    private float dashDuration; // calculado desde distancia/velocidad
    private Vector2 dashDir = Vector2.zero;

    private void Awake() {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = Mathf.Max(0.1f, rb.gravityScale);
        facingRight = faceRightDefault;
        dashCharges = dashMaxCharges;
        dashDuration = Mathf.Max(0.01f, dashDistance / Mathf.Max(0.01f, dashSpeed));
    }

    private void Update() {
        ReadHorizontalInput();
        HandleFacing();

        // Ground check (en Update para responsividad visual; el movimiento va en FixedUpdate)
        isGrounded = CheckGrounded();
        if (isGrounded) {
            canDoubleJump = true;
        }

        HandleJumpInput();
        HandleDashInput();
        RegenerateDashChargesIfNeeded();



    }

    private void FixedUpdate() {
        if (isDashing) {
            // Movimiento de dash: sobrescribe control normal
            rb.velocity = Vector2.zero;
            rb.MovePosition(rb.position + dashDir * dashSpeed * Time.fixedDeltaTime);
            return;
        }

        // Movimiento horizontal sin “inercia”: ajustamos vel X hacia target con MoveTowards
        float targetSpeed = moveDir * baseSpeed * speedMultiplier;
        currentVelX = Mathf.MoveTowards(currentVelX, targetSpeed, accel * Time.fixedDeltaTime);

        // Aplicar manteniendo la vel Y actual (física del salto)
        rb.velocity = new Vector2(currentVelX, rb.velocity.y);

        // Mario-style: caer más rápido y cortar salto al soltar
        if (!isDashing) {
            if (rb.velocity.y < 0f) {
                rb.velocity += Vector2.up * Physics2D.gravity.y * (fallGravityMultiplier - 1f) * Time.fixedDeltaTime;
            }
            else if (rb.velocity.y > 0f && !Input.GetKey(jumpKey)) {
                rb.velocity += Vector2.up * Physics2D.gravity.y * (lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
            }
        }

    }

    #region Input & Movement

    private void ReadHorizontalInput() {
        bool leftHeld = Input.GetKey(KeyCode.A);
        bool rightHeld = Input.GetKey(KeyCode.D);

        if (Input.GetKeyDown(KeyCode.A)) lastPressedDir = -1;
        if (Input.GetKeyDown(KeyCode.D)) lastPressedDir = 1;

        // Lógica: si se mantienen ambas, prioriza la última presionada.
        if (leftHeld && rightHeld) {
            moveDir = lastPressedDir;
        }
        else if (leftHeld) {
            moveDir = -1;
        }
        else if (rightHeld) {
            moveDir = 1;
        }
        else {
            moveDir = 0;
        }
    }

    private void HandleFacing() {
        if (moveDir != 0) {
            facingRight = moveDir > 0;
            // Si querés voltear sprite:
            // transform.localScale = new Vector3(facingRight ? 1 : -1, 1, 1);
        }
    }

    #endregion

    #region Ground Check

    private bool CheckGrounded() {
        Vector2 origin = (Vector2)transform.position + groundRayOffset;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundRayLength, groundMask);

        if (hit.collider != null) return true;

        // Tag fallback si no usás layers:
        RaycastHit2D hitNoMask = Physics2D.Raycast(origin, Vector2.down, groundRayLength);
        if (hitNoMask.collider != null && hitNoMask.collider.CompareTag(groundTag))
            return true;

        return false;
    }

    #endregion

    #region Jump (Charge + Double Jump)

    private void HandleJumpInput() {
        // iniciar carga
        if (Input.GetKeyDown(jumpKey)) {
            // Puede empezar carga si está en el suelo o si tiene doble salto disponible
            if (isGrounded || canDoubleJump) {
                isChargingJump = true;
                jumpChargeTimer = 0f;
            }
        }

        // manteniendo carga
        if (isChargingJump && Input.GetKey(jumpKey)) {
            jumpChargeTimer += Time.deltaTime;
            jumpChargeTimer = Mathf.Min(jumpChargeTimer, jumpMaxChargeTime);
        }

        // soltar o auto-lanzar al alcanzar max
        if (isChargingJump && (Input.GetKeyUp(jumpKey) || jumpChargeTimer >= jumpMaxChargeTime)) {
            float t = (jumpMaxChargeTime <= 0f) ? 1f : (jumpChargeTimer / jumpMaxChargeTime);
            float force = Mathf.Lerp(jumpMinForce, jumpMaxForce, t);

            // ¿Es doble salto?
            bool usingDoubleJump = (!isGrounded && canDoubleJump);

            // Si es segundo salto: cancelar TODA la velocidad vertical antes de aplicar la nueva fuerza
            if (usingDoubleJump) {
                rb.velocity = new Vector2(rb.velocity.x, 0f);
                canDoubleJump = false; // consumir segunda oportunidad
            }
            // Si es primer salto podés dejar la línea de abajo comentada si preferís NO resetear Y
            // rb.velocity = new Vector2(rb.velocity.x, 0f);

            rb.AddForce(Vector2.up * force, ForceMode2D.Impulse);

            isChargingJump = false;
            jumpChargeTimer = 0f;
        }

    }

    #endregion

    #region Dash (8-way, charges + cooldown)

    private void HandleDashInput() {
        if (!Input.GetKeyDown(dashKey)) return;
        if (isDashing) return;
        if (dashCharges <= 0) return;

        // Direccion de dash con flechas (8 direcciones)
        int x = 0, y = 0;
        if (Input.GetKey(KeyCode.D)) x = 1;
        if (Input.GetKey(KeyCode.A)) x = -1;
        if (Input.GetKey(KeyCode.W)) y = 1;
        if (Input.GetKey(KeyCode.S)) y = -1;

        Vector2 dir = new Vector2(x, y);

        if (dir == Vector2.zero) {
            // Si no hay dirección, usa facing
            dir = facingRight ? Vector2.right : Vector2.left;
        }
        else {
            dir = dir.normalized;
        }

        StartCoroutine(DashRoutine(dir));
    }

    private IEnumerator DashRoutine(Vector2 dir) {
        isDashing = true;
        dashDir = dir;
        dashCharges = Mathf.Max(0, dashCharges - 1);

        // Durante el dash, desactivar temporalmente la gravedad para precisión
        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;
        Vector2 originalVel = rb.velocity;
        rb.velocity = Vector2.zero;

        float t = 0f;
        while (t < dashDuration) {
            // Usamos MovePosition en FixedUpdate; acá forzamos pasos consistentes
            rb.MovePosition(rb.position + dashDir * dashSpeed * Time.fixedDeltaTime);
            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // Restaurar
        rb.gravityScale = originalGravity;
        rb.velocity = originalVel;
        isDashing = false;

        // Si gastaste todas las cargas, empieza el cooldown de recarga completa
        if (dashCharges == 0) {
            dashRegenTimer = dashCooldown;
            // Se recargan ambas al finalizar el cooldown
            StartCoroutine(DashRechargeRoutine());
        }
    }

    private void RegenerateDashChargesIfNeeded() {
        // Si aún quedan cargas, no hacemos nada.
        // La recarga completa se maneja en la corrutina cuando llega a 0.
    }

    private IEnumerator DashRechargeRoutine() {
        while (dashRegenTimer > 0f) {
            dashRegenTimer -= Time.deltaTime;
            yield return null;
        }
        dashCharges = dashMaxCharges;
    }

    #endregion

    private void OnDrawGizmosSelected() {
        if (!drawGizmos) return;
        Gizmos.color = Color.green;
        Vector2 origin = (Vector2)transform.position + groundRayOffset;
        Gizmos.DrawLine(origin, origin + Vector2.down * groundRayLength);
    }

    // ==== API pública útil desde el Editor o Inspector ====

    public void SetSpeedMultiplier(float multiplier) {
        speedMultiplier = Mathf.Max(0f, multiplier);
    }

    public float CurrentHorizontalSpeed => currentVelX;
    public bool IsGrounded => isGrounded;
    public int CurrentDashCharges => dashCharges;
}
