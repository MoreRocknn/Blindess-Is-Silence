using UnityEngine;

/// <summary>
/// Ponlo en el jugador. Emite pasos segun la velocidad real (funciona con cualquier
/// controlador e Input System / Input Manager, no lee teclas).
/// </summary>
public class NoiseMaker : MonoBehaviour
{
    [Header("Volumen (radio en metros)")]
    public float crouchLoudness = 3f;
    public float walkLoudness = 9f;
    public float runLoudness = 18f;

    [Header("Velocidades")]
    public float minMoveSpeed = 0.4f;
    public float runSpeedThreshold = 4f;

    [Header("Pasos")]
    public float stepLength = 1.6f;

    [Tooltip("Marcalo desde tu controlador cuando el jugador este agachado")]
    public bool crouching;

    Vector3 lastPos;
    float travelled;

    void Start() => lastPos = transform.position;

    void Update()
    {
        Vector3 delta = transform.position - lastPos;
        delta.y = 0f;
        lastPos = transform.position;

        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        if (speed < minMoveSpeed || speed > 40f) { travelled = 0f; return; } // parado o teletransporte

        bool running = speed >= runSpeedThreshold;
        travelled += delta.magnitude;
        float len = running ? stepLength * 1.3f : stepLength;
        if (travelled < len) return;
        travelled = 0f;

        float loud = crouching ? crouchLoudness : (running ? runLoudness : walkLoudness);
        SoundBus.Emit(transform.position, loud, running && !crouching ? SoundType.Run : SoundType.Footstep, gameObject);
    }

    /// <summary>Para ruidos puntuales: disparos, abrir puertas, gritos...</summary>
    public void MakeNoise(float loudness, SoundType type = SoundType.Other)
        => SoundBus.Emit(transform.position, loudness, type, gameObject);
}
