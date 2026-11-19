using System;
using UnityEngine;

public enum SoundType
{
    Footstep,     // pasos al caminar
    Run,          // pasos al correr
    Impact,       // golpes/caidas de objetos (accidentales)
    Thrown,       // objetos lanzados (posible señuelo)
    Voice,        // voz, gritos
    Interaction,  // puertas, interruptores...
    Other
}

public readonly struct SoundEvent
{
    public readonly Vector3 position;
    public readonly float loudness;   // radio (m) en el que se oye al 100% sin obstaculos
    public readonly SoundType type;
    public readonly GameObject source;
    public readonly float time;

    public SoundEvent(Vector3 position, float loudness, SoundType type, GameObject source)
    {
        this.position = position;
        this.loudness = loudness;
        this.type = type;
        this.source = source;
        this.time = Time.time;
    }
}

/// <summary>Cualquier script puede emitir un sonido: SoundBus.Emit(pos, 10f, SoundType.Impact, gameObject);</summary>
public static class SoundBus
{
    public static event Action<SoundEvent> OnSound;

    public static void Emit(Vector3 position, float loudness, SoundType type, GameObject source = null)
    {
        OnSound?.Invoke(new SoundEvent(position, loudness, type, source));
    }
}
