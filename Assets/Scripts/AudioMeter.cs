using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Barras tipo ecualizador que suben segun el ruido del jugador (correr alto, caminar medio, agachado poco).
/// Cada barra oscila por su cuenta, asi no es una barra recta sino un espectro que se mueve.
/// Para mas barras: duplica un sprite y arrastralo a 'Barras Extra'.
/// </summary>
public class AudioMeter : MonoBehaviour
{
    public PasosPersonaje pasos;               // vacio = se busca solo
    public SpriteRenderer spriteRenderer;      // barra 1 (L)
    public SpriteRenderer spriteDerecho;       // barra 2 (R)
    public SpriteRenderer[] barrasExtra;       // opcional: mas barras
    public float alturaMaxima = 1f;

    [Header("Efecto espectro")]
    [Tooltip("Cuanto baja cada barra respecto al nivel (0 = barras rectas, 1 = salta de 0 al maximo).")]
    [Range(0f, 1f)] public float variacion = 0.5f;
    [Tooltip("Velocidad del temblor. Mas alto = mas nervioso.")]
    public float velocidadOscilacion = 10f;
    [Tooltip("Pequeño latido cuando no hay ruido, para que la barra no se vea muerta.")]
    [Range(0f, 0.2f)] public float alturaReposo = 0.03f;
    [Tooltip("0 = movimiento suave. 6-10 = por escalones, estilo retro / LED.")]
    [Range(0, 16)] public int segmentos = 0;

    [Header("Suavizado del nivel")]
    [Range(1f, 100f)] public float velocidadSubida = 8f;
    [Range(1f, 30f)] public float suavizado = 6f;

    /// <summary>Ruido actual del jugador (0..1).</summary>
    public float Nivel { get; private set; }

    private readonly List<SpriteRenderer> barras = new List<SpriteRenderer>();

    private void Start()
    {
        if (pasos == null) pasos = FindFirstObjectByType<PasosPersonaje>();

        Anadir(spriteRenderer);
        Anadir(spriteDerecho);
        if (barrasExtra != null) foreach (SpriteRenderer b in barrasExtra) Anadir(b);
    }

    private void Anadir(SpriteRenderer s)
    {
        if (s == null) return;
        s.drawMode = SpriteDrawMode.Tiled;
        barras.Add(s);
    }

    private void Update()
    {
        float objetivo = pasos != null ? pasos.Ruido : 0f;   // los niveles se ajustan en PasosPersonaje
        float v = objetivo > Nivel ? velocidadSubida : suavizado;   // sube rapido, baja suave
        Nivel = Mathf.Lerp(Nivel, objetivo, 1f - Mathf.Exp(-v * Time.deltaTime));

        for (int i = 0; i < barras.Count; i++)
        {
            // Ruido distinto en cada barra (0..1): hace que suban y bajen por separado
            float r = Mathf.PerlinNoise(Time.time * velocidadOscilacion, i * 10.37f);

            float h = Mathf.Max(Nivel * Mathf.Lerp(1f - variacion, 1f, r), alturaReposo * r);
            if (segmentos > 0) h = Mathf.Round(h * segmentos) / segmentos;

            Vector2 size = barras[i].size;
            barras[i].size = new Vector2(size.x, h * alturaMaxima);
        }
    }
}