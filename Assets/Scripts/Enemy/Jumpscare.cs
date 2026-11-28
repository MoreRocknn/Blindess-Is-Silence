using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using StarterAssets;

/// <summary>
/// Jumpscare cuando el monstruo atrapa al jugador y despues pasa a la escena de Game Over.
/// Poner en el monstruo y conectar en BlindEnemyAI:  On Attack Player () > Jumpscare.Iniciar
/// </summary>
public class Jumpscare : MonoBehaviour
{
    /// <summary>Escena a la que vuelve el boton Reintentar.</summary>
    public static string UltimaEscena;

    [Header("Referencias")]
    [Tooltip("Camara hija del monstruo (a la altura de su cara, mirando hacia delante). Debe estar APAGADA.")]
    public GameObject camaraJumpscare;

    [Tooltip("Animator del monstruo (trigger 'Jumpscare'). Vacio = se busca en los hijos.")]
    public Animator animador;

    [Header("Sonido")]
    public AudioClip sonido;
    [Range(0f, 1f)] public float volumen = 1f;

    [Header("Ajustes")]
    [Tooltip("Segundos que dura el susto antes de pasar a Game Over.")]
    public float duracion = 2.5f;
    public string escenaGameOver = "GameOver";

    private bool activo;

    public void Iniciar()
    {
        if (activo) return;
        activo = true;
        StartCoroutine(Secuencia());
    }

    private IEnumerator Secuencia()
    {
        // 1) Congela al jugador y sus pasos
        FirstPersonController fpc = FindFirstObjectByType<FirstPersonController>();
        if (fpc != null) fpc.enabled = false;

        PasosPersonaje pasos = FindFirstObjectByType<PasosPersonaje>();
        if (pasos != null) { pasos.enabled = false; pasos.Source.Stop(); }

        // 2) Congela la IA del monstruo (la animacion sigue)
        BlindEnemyAI ia = GetComponent<BlindEnemyAI>();
        if (ia != null) ia.enabled = false;

        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null) { agent.isStopped = true; agent.ResetPath(); }

        // 3) Camara del susto + animacion + sonido
        Camera principal = Camera.main;
        if (principal != null) principal.enabled = false;      // el AudioListener sigue activo

        Vector3 oido = camaraJumpscare != null ? camaraJumpscare.transform.position : transform.position;
        if (camaraJumpscare != null) camaraJumpscare.SetActive(true);

        if (animador == null) animador = GetComponentInChildren<Animator>();
        if (animador != null) animador.SetTrigger("Jumpscare");

        if (sonido != null) AudioSource.PlayClipAtPoint(sonido, oido, volumen);

        // 4) Tras el susto, a la pantalla de Game Over
        yield return new WaitForSeconds(duracion);

        UltimaEscena = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(escenaGameOver);
    }
}
