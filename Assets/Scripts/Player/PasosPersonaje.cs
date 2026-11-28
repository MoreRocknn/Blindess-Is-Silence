using UnityEngine;
using UnityEngine.Serialization;
using StarterAssets;

/// <summary>
/// Pasos continuos con 3 estados (caminar / correr / agachado) y fundidos suaves.
/// Poner en el mismo objeto que el jugador (con FirstPersonController).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PasosPersonaje : MonoBehaviour
{
    [Header("Clips en bucle")]
    [FormerlySerializedAs("clipCaminataContinua")] public AudioClip clipCaminar;
    public AudioClip clipCorrer;      // vacio = usa el de caminar
    public AudioClip clipAgachado;    // vacio = usa el de caminar

    [Header("Ajustes")]
    [Range(0f, 1f)] public float volumen = 1f;
    public float velocidadMinima = 0.2f;
    [Tooltip("Solo si el objeto no tiene FirstPersonController (ej. el monstruo).")]
    public float velocidadCorriendo = 4.5f;

    [Header("Ruido que hace el jugador (0 a 1)")]
    [Tooltip("Lo muestra la barra y lo oye el monstruo.")]
    [Range(0f, 1f)] public float nivelCorrer = 1f;
    [Range(0f, 1f)] public float nivelCaminar = 0.5f;
    [Range(0f, 1f)] public float nivelAgachado = 0.2f;

    [Header("Transiciones (segundos)")]
    [Min(0f)] public float tiempoFadeIn = 0.15f;
    [Min(0f)] public float tiempoFadeOut = 0.35f;
    [Min(0f)] public float tiempoCambioClip = 0.12f;

    public bool Caminando { get; private set; }
    public bool Corriendo { get; private set; }
    public bool Agachado { get; private set; }
    public float Ruido { get; private set; }        // 0 quieto .. 1 corriendo
    public AudioSource Source { get; private set; }

    private CharacterController cc;
    private FirstPersonController fpc;
    private StarterAssetsInputs input;
    private Vector3 ultimaPos;
    private float fade;

    private void Awake()
    {
        Source = GetComponent<AudioSource>();
        Source.playOnAwake = false;
        Source.loop = true;
        Source.pitch = 1f;

        cc = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
        input = GetComponent<StarterAssetsInputs>();
        ultimaPos = transform.position;
    }

    private void Update()
    {
        Vector3 v = cc != null ? cc.velocity : (transform.position - ultimaPos) / Mathf.Max(Time.deltaTime, 0.0001f);
        ultimaPos = transform.position;
        v.y = 0f;

        Caminando = v.magnitude >= velocidadMinima;
        Agachado = fpc != null && fpc.IsCrouching;
        Corriendo = Caminando && !Agachado && (input != null ? input.sprint : v.magnitude >= velocidadCorriendo);

        Ruido = !Caminando ? 0f : (Agachado ? nivelAgachado : (Corriendo ? nivelCorrer : nivelCaminar));

        Fundido();
    }

    private AudioClip ClipDelEstado()
    {
        AudioClip c = Agachado ? clipAgachado : (Corriendo ? clipCorrer : clipCaminar);
        return c != null ? c : clipCaminar;
    }

    private void Fundido()
    {
        AudioClip deseado = Caminando ? ClipDelEstado() : null;
        float objetivo = deseado != null ? 1f : 0f;
        float duracion = deseado != null ? tiempoFadeIn : tiempoFadeOut;

        if (deseado != null && (Source.clip != deseado || !Source.isPlaying))
        {
            if (!Source.isPlaying) fade = 0f;                    // arranca desde silencio

            if (fade <= 0.02f) { Source.clip = deseado; Source.Play(); }
            else { objetivo = 0f; duracion = tiempoCambioClip; } // baja antes de cambiar de clip
        }

        fade = Mathf.MoveTowards(fade, objetivo, Time.deltaTime / Mathf.Max(duracion, 0.001f));
        Source.volume = volumen * fade;

        if (deseado == null && fade <= 0f && Source.isPlaying) Source.Stop();
    }
}