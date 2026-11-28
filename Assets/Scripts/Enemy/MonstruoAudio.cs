using UnityEngine;

/// <summary>
/// Sonido del monstruo segun lo que hace su IA. Poner en el mismo objeto que BlindEnemyAI.
/// Crea solo los AudioSource que necesita: no hay que montar nada a mano.
///  - Gruñido en bucle (3D): mas fuerte y grave/agudo segun lo alerta que este.
///  - Grito al lanzarse a cazar, y golpe al atacar.
///  - Musica de persecucion (2D) que entra y sale con fundido.
/// </summary>
[RequireComponent(typeof(BlindEnemyAI))]
public class MonstruoAudio : MonoBehaviour
{
    [Header("Clips")]
    public AudioClip clipGrunido;    // monster-growl (bucle)
    public AudioClip clipGrito;      // flying-monster-screech (al detectarte)
    public AudioClip clipGolpe;      // horror-impact-hit (al atacar)
    public AudioClip clipMusica;     // chased-by-shadows (mientras te caza)

    [Header("Volumen")]
    [Range(0f, 1f)] public float volGrunido = 0.8f;
    [Range(0f, 1f)] public float volGrito = 1f;
    [Range(0f, 1f)] public float volGolpe = 1f;
    [Range(0f, 1f)] public float volMusica = 0.5f;

    [Header("Gruñido segun alerta")]
    public float pitchCalma = 0.9f;
    public float pitchCaza = 1.1f;
    [Tooltip("Parte del volumen del gruñido cuando esta tranquilo.")]
    [Range(0f, 1f)] public float volumenCalma = 0.4f;

    [Header("Otros")]
    [Tooltip("Segundos minimos entre dos gritos.")]
    public float cooldownGrito = 10f;
    public float fadeInMusica = 1.5f;
    public float fadeOutMusica = 3f;
    public float distanciaMin = 3f;
    public float distanciaMax = 30f;

    private BlindEnemyAI ai;
    private AudioSource grunido, efectos, musica;
    private BlindEnemyAI.State anterior;
    private float ultimoGrito = -999f;

    private void Awake()
    {
        ai = GetComponent<BlindEnemyAI>();
        grunido = Crear(true, 1f, clipGrunido);
        efectos = Crear(false, 1f, null);
        musica = Crear(true, 0f, clipMusica);   // 2D: se oye igual en todas partes
        grunido.volume = volGrunido * volumenCalma;
        musica.volume = 0f;
    }

    private void Start()
    {
        anterior = ai.Current;
        if (clipGrunido != null) grunido.Play();
    }

    private AudioSource Crear(bool loop, float spatial, AudioClip clip)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = spatial;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = distanciaMin;
        s.maxDistance = distanciaMax;
        return s;
    }

    private void Update()
    {
        BlindEnemyAI.State s = ai.Current;
        if (s != anterior) { AlCambiarEstado(anterior, s); anterior = s; }

        bool caza = s == BlindEnemyAI.State.Hunt || s == BlindEnemyAI.State.Attack;
        float dt = Time.deltaTime;

        // Gruñido: 0 = tranquilo, 1 = cazando
        float k = caza ? 1f : Mathf.Clamp01(ai.Alert);
        grunido.volume = Mathf.Lerp(grunido.volume, volGrunido * Mathf.Lerp(volumenCalma, 1f, k), dt * 3f);
        grunido.pitch = Mathf.Lerp(grunido.pitch, Mathf.Lerp(pitchCalma, pitchCaza, k), dt * 3f);

        // Musica: entra al cazar y sale suave al perderte
        if (clipMusica != null)
        {
            float objetivo = caza ? volMusica : 0f;
            float paso = volMusica * dt / Mathf.Max(caza ? fadeInMusica : fadeOutMusica, 0.01f);
            musica.volume = Mathf.MoveTowards(musica.volume, objetivo, paso);

            if (musica.volume > 0f && !musica.isPlaying) musica.Play();
            else if (musica.volume <= 0f && musica.isPlaying) musica.Stop();
        }
    }

    private void AlCambiarEstado(BlindEnemyAI.State antes, BlindEnemyAI.State ahora)
    {
        if (ahora == BlindEnemyAI.State.Hunt && antes != BlindEnemyAI.State.Attack
            && clipGrito != null && Time.time - ultimoGrito >= cooldownGrito)
        {
            ultimoGrito = Time.time;
            efectos.PlayOneShot(clipGrito, volGrito);
        }

        if (ahora == BlindEnemyAI.State.Attack && clipGolpe != null)
            efectos.PlayOneShot(clipGolpe, volGolpe);
    }
}
