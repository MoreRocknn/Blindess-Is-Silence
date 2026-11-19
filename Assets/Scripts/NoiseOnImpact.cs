using UnityEngine;

/// <summary>
/// Ponlo en objetos con Collider (botellas, cajas, piedras...). Al chocar emiten sonido.
/// Si el jugador lo lanza, pon isThrownDecoy = true para que el enemigo pueda aprender a ignorar señuelos.
/// </summary>
public class NoiseOnImpact : MonoBehaviour
{
    public float loudnessPerImpulse = 2.5f;
    public float minImpulse = 1.5f;
    public float maxLoudness = 25f;
    public bool isThrownDecoy;

    float nextAllowed;

    void OnCollisionEnter(Collision c)
    {
        if (Time.time < nextAllowed) return;
        float impulse = c.impulse.magnitude;
        if (impulse < minImpulse) return;

        nextAllowed = Time.time + 0.25f;
        Vector3 p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        SoundBus.Emit(p, Mathf.Min(impulse * loudnessPerImpulse, maxLoudness),
                      isThrownDecoy ? SoundType.Thrown : SoundType.Impact, gameObject);
    }
}
