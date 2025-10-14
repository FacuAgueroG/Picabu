using UnityEngine;
using System.Collections;

public class simpleAttack : MonoBehaviour {
    [Header("Entrada (opcional si no usás simpleControls)")]
    public KeyCode attackKey = KeyCode.S;       // Si no usás simpleControls
    public bool useLocalKey = true;             // true = lee attackKey; false = usa controls.AttackDown()

    [Header("Referencias")]
    public simpleControls controls;             // opcional: si querés centralizar input
    public Collider2D areaCollider;             // collider del hitbox (isTrigger = true)
    public GameObject areaVisual;               // opcional: sprite/GO visual del hit

    [Header("Tiempos")]
    [Min(0.01f)] public float activeTime = 0.20f;
    [Min(0f)] public float cooldownTime = 0.20f;

    // Estado
    bool isActive = false;
    bool inCooldown = false;
    Coroutine routine;
    simpleAttackSensor sensor;

    void Reset() {
        if (areaCollider == null) areaCollider = GetComponent<Collider2D>();
        if (areaCollider != null) areaCollider.isTrigger = true;
        areaVisual = areaVisual ?? gameObject;
    }

    void Awake() {
        if (areaCollider == null) areaCollider = GetComponent<Collider2D>();
        if (areaCollider != null) areaCollider.isTrigger = true;
        // Hitbox siempre activo (GameObject), pero collider arrancará apagado
        if (areaCollider != null) areaCollider.enabled = false;

        sensor = GetComponent<simpleAttackSensor>();
        if (sensor == null) sensor = GetComponentInChildren<simpleAttackSensor>(true);

        // Si no hay visual dedicado, no pasa nada
        if (areaVisual != null) areaVisual.SetActive(false);
    }

    void Update() {
        bool pressed = useLocalKey ? Input.GetKeyDown(attackKey)
                                   : (controls != null && controls.AttackDown());

        if (pressed && !isActive && !inCooldown) {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(DoAttackWindow());
        }
    }

    IEnumerator DoAttackWindow() {
        isActive = true;

        if (areaVisual != null) areaVisual.SetActive(true);
        // Alinear con el ciclo de física
        yield return new WaitForFixedUpdate();

        // Marcar inicio de ventana en el sensor (limpia set de golpeados)
        if (sensor != null) sensor.BeginWindow();

        // Encender collider
        if (areaCollider != null) areaCollider.enabled = true;

        // Mantener la ventana
        yield return new WaitForSeconds(activeTime);

        // Apagar collider
        if (areaCollider != null) areaCollider.enabled = false;

        // Fin de ventana
        if (sensor != null) sensor.EndWindow();

        if (areaVisual != null) areaVisual.SetActive(false);
        isActive = false;

        // Cooldown
        inCooldown = true;
        yield return new WaitForSeconds(cooldownTime);
        inCooldown = false;
        routine = null;
    }
}
