using UnityEngine;

/// <summary>
/// Barras L y R que suben segun el ruido del jugador:
/// correr = muy alto, caminar = medio, agachado = poco, quieto = nada.
/// </summary>
public class AudioMeter : MonoBehaviour
{
    public PasosPersonaje pasos;               // vacio = se busca solo
    public SpriteRenderer spriteRenderer;      // barra L
    public SpriteRenderer spriteDerecho;       // barra R
    public float alturaMaxima = 1f;

    [Header("Suavizado")]
    [Range(1f, 100f)] public float velocidadSubida = 8f;
    [Range(1f, 30f)] public float suavizado = 6f;

    /// <summary>Ruido actual del jugador (0..1). Lo puede leer la IA del monstruo.</summary>
    public float Nivel { get; private set; }

    private void Start()
    {
        if (pasos == null) pasos = FindFirstObjectByType<PasosPersonaje>();
        if (spriteRenderer != null) spriteRenderer.drawMode = SpriteDrawMode.Tiled;
        if (spriteDerecho != null) spriteDerecho.drawMode = SpriteDrawMode.Tiled;
    }

    private void Update()
    {
        float objetivo = pasos != null ? pasos.Ruido : 0f;   // los niveles se ajustan en PasosPersonaje

        float v = objetivo > Nivel ? velocidadSubida : suavizado;   // sube rapido, baja suave
        Nivel = Mathf.Lerp(Nivel, objetivo, 1f - Mathf.Exp(-v * Time.deltaTime));

        Barra(spriteRenderer);
        Barra(spriteDerecho);
    }

    private void Barra(SpriteRenderer s)
    {
        if (s != null) s.size = new Vector2(s.size.x, Nivel * alturaMaxima);
    }
}