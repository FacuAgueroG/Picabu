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

    [Header("Ataque (nuevo)")]
    public KeyCode attack = KeyCode.S;

    // Devuelve -1, 0 o +1 (muy simple)
    public int MoveAxis() {
        if (blockInput) return 0;

        bool l = Input.GetKey(leftAlt);
        bool r = Input.GetKey(rightAlt);

        if (l == r) return 0; // ninguno o ambos
        return r ? 1 : -1;
    }

    // Lecturas simples
    public bool JumpHeld() => !blockInput && Input.GetKey(jump);
    public bool DashHeld() => !blockInput && Input.GetKey(dash);
    public bool JumpDown() => !blockInput && Input.GetKeyDown(jump);
    public bool DashDown() => !blockInput && Input.GetKeyDown(dash);

    // Nuevo:
    public bool AttackDown() => !blockInput && Input.GetKeyDown(attack);
}
