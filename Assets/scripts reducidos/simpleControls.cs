using UnityEngine;

public class simpleControls : MonoBehaviour {
    [Header("Bloqueo global de input")]
    public bool blockInput = false;

    [Header("Movimiento")]
    public KeyCode leftAlt = KeyCode.LeftArrow;
    public KeyCode rightAlt = KeyCode.RightArrow;

    [Header("Acciones (para después)")]
    public KeyCode jump = KeyCode.Space;
    public KeyCode dash = KeyCode.LeftShift;

    [Header("Ataques")]
    public KeyCode attackS = KeyCode.S; // Ligero (circular)
    public KeyCode attackD = KeyCode.D; // Rectangular (frente/atrás)

    [Header("Modificador (aire)")]
    [Tooltip("Mantener para forzar air-stall en el aire al presionar S o D.")]
    public KeyCode stallModifier = KeyCode.LeftControl;

    // Eje horizontal muy simple
    public int MoveAxis() {
        if (blockInput) return 0;
        bool l = Input.GetKey(leftAlt);
        bool r = Input.GetKey(rightAlt);
        if (l == r) return 0;
        return r ? 1 : -1;
    }

    // Lecturas simples
    public bool JumpHeld() => !blockInput && Input.GetKey(jump);
    public bool DashHeld() => !blockInput && Input.GetKey(dash);
    public bool JumpDown() => !blockInput && Input.GetKeyDown(jump);
    public bool DashDown() => !blockInput && Input.GetKeyDown(dash);

    // Ataques
    public bool AttackSDown() => !blockInput && Input.GetKeyDown(attackS);
    public bool AttackSHeld() => !blockInput && Input.GetKey(attackS);
    public bool AttackSUp() => !blockInput && Input.GetKeyUp(attackS);

    public bool AttackDDown() => !blockInput && Input.GetKeyDown(attackD);
    public bool AttackDHeld() => !blockInput && Input.GetKey(attackD);

    // Modificador de air-stall
    public bool StallHeld() => !blockInput && Input.GetKey(stallModifier);
}
