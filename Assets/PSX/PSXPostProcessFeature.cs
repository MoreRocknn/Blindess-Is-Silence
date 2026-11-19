using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renderer Feature de post-proceso PSX para URP en Unity 6 (Render Graph).
/// Pixelado + reduccion de color + dithering en un solo pass.
/// </summary>
public class PSXPostProcessFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Tooltip("Resolucion vertical simulada (el ancho se calcula con el aspect). 0 = sin pixelado.")]
        [Min(0)] public int verticalResolution = 240;

        [Tooltip("Bits por canal de color. PS1 = 5.")]
        [Range(1, 8)] public int bitsPerChannel = 5;

        [Tooltip("Fuerza del dithering ordenado (0 = sin dither).")]
        [Range(0f, 1f)] public float ditherStrength = 1f;
    }

    [Tooltip("Opcional: asigna Hidden/PSX/PostProcess. Si queda vacio se busca por nombre.")]
    public Shader shader;
    public Settings settings = new Settings();

    Material material;
    PSXPass pass;

    public override void Create()
    {
        if (shader == null)
            shader = Shader.Find("Hidden/PSX/PostProcess");

        if (shader == null)
        {
            Debug.LogWarning("PSXPostProcessFeature: no se encontro el shader Hidden/PSX/PostProcess.");
            pass = null;
            return;
        }

        material = CoreUtils.CreateEngineMaterial(shader);
        pass = new PSXPass(material);
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

    class PSXPass : ScriptableRenderPass
    {
        static readonly int ParamsId = Shader.PropertyToID("_PSXParams");

        readonly Material mat;
        Vector4 parameters;

        public PSXPass(Material material)
        {
            mat = material;
            requiresIntermediateTexture = true; // necesitamos leer y escribir el color de camara
        }

        public void Configure(Settings s)
        {
            parameters = new Vector4(s.verticalResolution, s.bitsPerChannel, s.ditherStrength, 0f);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle source = resourceData.activeColorTexture;

            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_PSXPostProcessTemp";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            mat.SetVector(ParamsId, parameters);

            var blit = new RenderGraphUtils.BlitMaterialParameters(source, destination, mat, 0);
            renderGraph.AddBlitPass(blit, "PSX Post Process");

            resourceData.cameraColor = destination;
        }
    }
}
