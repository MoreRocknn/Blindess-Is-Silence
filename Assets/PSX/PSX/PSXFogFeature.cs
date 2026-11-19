using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Niebla en espacio de pantalla estilo PSX para URP en Unity 6 (Render Graph).
/// Ponla ANTES de PSX Post Process Feature (para que el pixelado/dither la afecte).
/// Requiere "Depth Texture" activado en el URP Asset.
/// </summary>
public class PSXFogFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        public Color fogColor = new Color(0.5f, 0.5f, 0.55f, 1f);
        [Min(0)] public float startDistance = 5f;
        [Min(0.01f)] public float endDistance = 40f;
        [Range(0f, 1f)] public float intensity = 1f;

        [Tooltip("Numero de bandas de niebla. 0 = degradado continuo.")]
        [Min(0)] public int bands = 8;

        [Tooltip("Aplica niebla tambien al cielo.")]
        public bool affectSky = false;
    }

    [Tooltip("Opcional: asigna Hidden/PSX/Fog. Si queda vacio se busca por nombre.")]
    public Shader shader;
    public Settings settings = new Settings();

    Material material;
    FogPass pass;

    public override void Create()
    {
        if (shader == null)
            shader = Shader.Find("Hidden/PSX/Fog");

        if (shader == null)
        {
            Debug.LogWarning("PSXFogFeature: no se encontro el shader Hidden/PSX/Fog.");
            pass = null;
            return;
        }

        material = CoreUtils.CreateEngineMaterial(shader);
        pass = new FogPass(material);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null) return;

        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;

        pass.renderPassEvent = settings.renderPassEvent;
        pass.Configure(settings);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
    }

    class FogPass : ScriptableRenderPass
    {
        static readonly int ColorId = Shader.PropertyToID("_PSXFogColor");
        static readonly int ParamsId = Shader.PropertyToID("_PSXFogParams");
        static readonly int Params2Id = Shader.PropertyToID("_PSXFogParams2");
        static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepthTexture");

        class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        readonly Material mat;
        Color color;
        Vector4 p1, p2;

        public FogPass(Material material)
        {
            mat = material;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth); // pide la textura de profundidad
        }

        public void Configure(Settings s)
        {
            color = s.fogColor;
            p1 = new Vector4(s.startDistance, Mathf.Max(s.endDistance, s.startDistance + 0.01f), s.intensity, s.bands);
            p2 = new Vector4(s.affectSky ? 1f : 0f, 0f, 0f, 0f);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle source = resourceData.activeColorTexture;

            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_PSXFogTemp";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            mat.SetColor(ColorId, color);
            mat.SetVector(ParamsId, p1);
            mat.SetVector(Params2Id, p2);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("PSX Fog", out var data))
            {
                data.source = source;
                data.material = mat;

                builder.UseTexture(source);
                builder.UseGlobalTexture(CameraDepthId); // el shader lee _CameraDepthTexture
                builder.SetRenderAttachment(destination, 0);

                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }
}
