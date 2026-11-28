using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(BlindEnemyAI))]
public class MonstruoPasos : MonoBehaviour
{
    [Header("Sonidos")]
    public AudioClip[] pasosAndar;
    public AudioClip[] pasosCorrer;
    [Range(0f, 1f)] public float volumen = 0.8f;
    public Vector2 variacionPitch = new Vector2(0.9f, 1.1f);

    [Header("Cadencia (segundos entre pasos)")]
    public float intervaloAndar = 0.7f;
    public float intervaloCorrer = 0.35f;

    [Header("Umbrales de velocidad")]
    public float velocidadMinima = 0.3f;     // por debajo = quieto, sin sonido
    public float velocidadCorrer = 3.5f;     // a partir de aqui suena "correr"

    private NavMeshAgent agent;
    private BlindEnemyAI ai;
    private AudioSource fuente;
    private float timer;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ai = GetComponent<BlindEnemyAI>();

        fuente = gameObject.AddComponent<AudioSource>();
        fuente.spatialBlend = 1f;            // sonido 3D
        fuente.minDistance = 2f;
        fuente.maxDistance = 25f;
        fuente.rolloffMode = AudioRolloffMode.Linear;
        fuente.playOnAwake = false;
    }

    private void Update()
    {
        float vel = agent.velocity.magnitude;

        if (ai.Roaring || vel < velocidadMinima)
        {
            timer = 0f;                      // el primer paso suena al instante al arrancar
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        bool corriendo = vel >= velocidadCorrer;
        AudioClip[] clips = corriendo ? pasosCorrer : pasosAndar;
        if (clips == null || clips.Length == 0) return;

        fuente.pitch = Random.Range(variacionPitch.x, variacionPitch.y);
        fuente.PlayOneShot(clips[Random.Range(0, clips.Length)], volumen);

        timer = corriendo ? intervaloCorrer : intervaloAndar;
    }
}