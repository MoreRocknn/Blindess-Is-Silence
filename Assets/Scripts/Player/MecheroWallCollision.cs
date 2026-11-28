using UnityEngine;

public class MecheroWallCollision : MonoBehaviour
{
    [Header("Referencias")]
    public Transform camara;
    public Transform wallCheck;
    public Transform llama; // Objeto con el ParticleSystem (si es hijo del mechero, puedes dejarlo vacío)

    [Header("Deteccion")]
    public float distanciaDeteccion = 0.6f;
    public float distanciaSeguridad = 0.05f;
    public LayerMask capasParedes;

    [Header("Movimiento")]
    public float retraccionMaxima = 0.4f;
    public float velocidad = 12f;

    private Vector3 posicionInicial;
    private Quaternion rotacionInicial;

    private Vector3 llamaPosicionInicial;
    private Quaternion llamaRotacionInicial;

    void Start()
    {
        posicionInicial = transform.localPosition;
        rotacionInicial = transform.localRotation;

        if (llama != null)
        {
            llamaPosicionInicial = llama.localPosition;
            llamaRotacionInicial = llama.localRotation;
        }
    }

    void Update()
    {
        float retraccion = 0f;

        // Raycast desde el Empty hacia delante
        if (Physics.Raycast(
            wallCheck.position,
            camara.forward,
            out RaycastHit hit,
            distanciaDeteccion,
            capasParedes,
            QueryTriggerInteraction.Ignore))
        {
            retraccion = distanciaDeteccion - hit.distance
                        + distanciaSeguridad;

            retraccion = Mathf.Clamp(
                retraccion,
                0f,
                retraccionMaxima
            );
        }

        // Mechero
        MoverRetraido(transform, posicionInicial, rotacionInicial, retraccion);

        // Llama (solo si es un objeto independiente del mechero)
        if (llama != null)
        {
            MoverRetraido(llama, llamaPosicionInicial, llamaRotacionInicial, retraccion);
        }
    }

    private void MoverRetraido(Transform objeto, Vector3 posLocalInicial, Quaternion rotLocalInicial, float retraccion)
    {
        Transform padre = objeto.parent;

        // Posicion normal
        Vector3 posicionNormal = padre.TransformPoint(posLocalInicial);

        // Retraccion hacia atras respecto a la camara
        Vector3 posicionObjetivo = posicionNormal - camara.forward * retraccion;

        // Convertir a posicion local
        Vector3 posicionLocal = padre.InverseTransformPoint(posicionObjetivo);

        // Movimiento suave
        objeto.localPosition = Vector3.Lerp(
            objeto.localPosition,
            posicionLocal,
            Time.deltaTime * velocidad
        );

        objeto.localRotation = Quaternion.Lerp(
            objeto.localRotation,
            rotLocalInicial,
            Time.deltaTime * velocidad
        );
    }

    void OnDrawGizmos()
    {
        if (wallCheck != null && camara != null)
        {
            Gizmos.color = Color.yellow;

            Gizmos.DrawLine(
                wallCheck.position,
                wallCheck.position + camara.forward * distanciaDeteccion
            );
        }
    }
}