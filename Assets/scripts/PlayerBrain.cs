using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlayerBrain : MonoBehaviour {
    [Header("Input Keys")]
    public KeyCode leftKey = KeyCode.A;
    public KeyCode rightKey = KeyCode.D;
    public KeyCode upKey = KeyCode.W;
    public KeyCode downKey = KeyCode.S;
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode dashKey = KeyCode.LeftShift;

    [Header("Sprite Flip (optional)")]
    public Transform spriteChild;
    public bool faceRightDefault = true;

    // Shared refs/state
    [HideInInspector] public Rigidbody2D rb;
    [HideInInspector] public Collider2D col;

    // Input snapshot
    [HideInInspector] public int moveX;           // -1 / 0 / +1
    [HideInInspector] public bool holdLeft;
    [HideInInspector] public bool holdRight;
    [HideInInspector] public bool holdUp;
    [HideInInspector] public bool holdDown;
    [HideInInspector] public bool jumpDown;
    [HideInInspector] public bool jumpUp;
    [HideInInspector] public bool jumpHeld;
    [HideInInspector] public bool dashDown;

    // Facing
    [HideInInspector] public bool facingRight;

    // Modules
    LocomotionMotor2D motor;
    Grounding2D grounding;
    WallModule2D wall;
    JumpModule2D jump;
    DashModule2D dash;
    GravityModule2D gravity;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.gravityScale = Mathf.Max(0.1f, rb.gravityScale);

        motor = GetComponent<LocomotionMotor2D>();
        grounding = GetComponent<Grounding2D>();
        wall = GetComponent<WallModule2D>();
        jump = GetComponent<JumpModule2D>();
        dash = GetComponent<DashModule2D>();
        gravity = GetComponent<GravityModule2D>();

        facingRight = faceRightDefault;
        if (motor) motor.InitSprite(spriteChild, () => facingRight, v => facingRight = v);
    }

    void Update() {
        // -------- Read inputs (frame) --------
        holdLeft = Input.GetKey(leftKey);
        holdRight = Input.GetKey(rightKey);
        holdUp = Input.GetKey(upKey);
        holdDown = Input.GetKey(downKey);

        jumpDown = Input.GetKeyDown(jumpKey);
        jumpUp = Input.GetKeyUp(jumpKey);
        jumpHeld = Input.GetKey(jumpKey);

        dashDown = Input.GetKeyDown(dashKey);

        // Resolve horizontal intent (last-press wins if both)
        if (holdLeft == holdRight) moveX = 0;
        else moveX = holdLeft ? -1 : 1;

        // >>>>>> SUPRESIÓN DE INPUT HACIA LA PARED (post-resolución de moveX) <<<<<<
        // Si el wall-jump activó supresión hacia la pared, neutralizamos esa dirección.
        if (wall) {
            if (wall.IsInputSuppressedTo(WallModule2D.WallSide.Left) && holdLeft)
                moveX = holdRight ? 1 : 0;

            if (wall.IsInputSuppressedTo(WallModule2D.WallSide.Right) && holdRight)
                moveX = holdLeft ? -1 : 0;
        }

        // -------- Sense world --------
        if (grounding) grounding.TickSense();           // ground + one-way memory
        if (wall) wall.TickSense();                     // wall side & states

        // -------- High-level actions --------
        if (dash) dash.HandleDashInput(dashDown);       // consumes charges/buffers
        if (jump) jump.HandleJumpInput(jumpDown, jumpUp, jumpHeld); // buffers, wall-jump, double, hold

        // -------- Horizontal locomotion & facing --------
        if (motor) motor.TickHorizontal(moveX, holdLeft, holdRight);

        // -------- Gravity & vertical rules (Fixed handles forces; here we set modes) --------
        if (gravity) gravity.TickModes();
    }

    void FixedUpdate() {
        // Dash moves via its own routines; motor/gravity run only if not “active dash”
        bool dashing = dash && dash.IsDashing;
        if (!dashing) {
            if (motor) motor.FixedTick(grounding);
            if (jump) jump.FixedTickHold();       // hold-to-height impulses
            if (gravity) gravity.FixedTick();      // Mario-like gravity ramps / wall slide gravity
        }
    }
}
