using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Efecto CRT para URP en Unity 6 (Render Graph).
/// Ponlo DEBAJO de PSX Post Process Feature en la lista del Renderer.
/// </summary>
public class PSXCRTFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Range(0f, 0.5f)] public float curvature = 0.12f;
        [Range(0f, 1f)] public float vignette = 0.5f;
        [Range(0f, 1f)] public float scanlines = 0.25f;
        [Range(0f, 0.2f)] public float noise = 0.03f;
        [Range(0f, 0.02f)] public float chromaticAberration = 0.003f;
        [Min(1)] public float scanlineCount = 240f;
    }

    [Tooltip("Opcional: asigna Hidden/PSX/CRT. Si queda vacio se busca por nombre.")]
    public Shader shader;
    public Settings settings = new Settings();

    Material material;
    CRTPass pass;

    public override void Create()
    {
        if (shader == null)
            shader = Shader.Find("Hidden/PSX/CRT");

        if (shader == null)
        {
            Debug.LogWarning("PSXCRTFeature: no se encontro el shader Hidden/PSX/CRT.");
            pass = null;
            return;
        }

        material = CoreUtils.CreateEngineMaterial(shader);
        pass = new CRTPass(material);
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

    class CRTPass : ScriptableRenderPass
    {
        static readonly int Params1Id = Shader.PropertyToID("_CRTParams1");
        static readonly int Params2Id = Shader.PropertyToID("_CRTParams2");

        readonly Material mat;
        Vector4 p1, p2;

        public CRTPass(Material material)
        {
            mat = material;
            requiresIntermediateTexture = true;
        }

        public void Configure(Settings s)
        {
            p1 = new Vector4(s.curvature, s.vignette, s.scanlines, s.noise);
            p2 = new Vector4(s.chromaticAberration, s.scanlineCount, 0f, 0f);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle source = resourceData.activeColorTexture;

            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_PSXCRTTemp";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            mat.SetVector(Params1Id, p1);
            mat.SetVector(Params2Id, p2);

            var blit = new RenderGraphUtils.BlitMaterialParameters(source, destination, mat, 0);
            renderGraph.AddBlitPass(blit, "PSX CRT");

            resourceData.cameraColor = destination;
        }
    }
}
