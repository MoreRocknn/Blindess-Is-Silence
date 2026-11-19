// PSX/Lit v2 - shader estilo PlayStation 1 para URP (Unity 6)
// - Vertex snapping (rejilla de baja resolucion en pantalla)
// - Affine texture mapping
// - Pixelado de textura (aplasta la resolucion de la textura)
// - Color precision por material (bits por canal)
// - Camera clip: descarta pixeles demasiado cerca de la camara
// - Iluminacion por vertice (Gouraud): luz principal, ambiente, luces adicionales
// - Specular opcional por vertice (Blinn-Phong)
// - Niebla por vertice
// - Blend configurable (para variante transparente)
Shader "PSX/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)

        [Header(PSX)]
        _SnapResolution("Vertex Snap Grid (x,y)", Vector) = (160, 120, 0, 0)
        _AffineStrength("Affine Texture Warp (>1 exagera)", Range(0,2)) = 1
        _SwimStrength("Texture Swim (se arrastra con el snap)", Range(0,30)) = 0
        _TextureResolution("Texture Pixelation (x,y) 0 = off", Vector) = (0, 0, 0, 0)
        _ColorBits("Color Bits per Channel (8 = off)", Range(1,8)) = 5
        _ClipDistance("Camera Clip Distance", Float) = 0

        [Header(Lighting)]
        _AmbientBoost("Ambient Boost", Range(0,2)) = 1
        [Toggle(_PSX_SPECULAR)] _Specular("Specular", Float) = 0
        [HDR] _SpecColor("Specular Color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.5

        [Header(Surface)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        // Opaco: One / Zero / ZWrite 1.  Transparente: SrcAlpha / OneMinusSrcAlpha / ZWrite 0 (+ Render Queue 3000)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap);
        // Sampler inline: filtro point + wrap repeat
        SAMPLER(sampler_PSXPointRepeat);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float4 _SnapResolution;
            float4 _TextureResolution;
            half4 _SpecColor;
            half _AffineStrength;
            half _SwimStrength;
            half _ColorBits;
            half _ClipDistance;
            half _AmbientBoost;
            half _Smoothness;
            half _Cutoff;
        CBUFFER_END

        // Redondea la posicion en clip space a una rejilla de pantalla
        float4 PSXSnap(float4 positionCS)
        {
            if (_SnapResolution.x > 0.0 && _SnapResolution.y > 0.0)
            {
                float2 grid = _SnapResolution.xy * 0.5; // NDC va de -1 a 1
                float2 ndc = positionCS.xy / positionCS.w;
                ndc = floor(ndc * grid + 0.5) / grid;
                positionCS.xy = ndc * positionCS.w;
            }
            return positionCS;
        }

        // Aplasta la resolucion de la textura
        float2 PSXTextureUV(float2 uv)
        {
            if (_TextureResolution.x > 0.0 && _TextureResolution.y > 0.0)
                uv = (floor(uv * _TextureResolution.xy) + 0.5) / _TextureResolution.xy;
            return uv;
        }
        ENDHLSL

        // ------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            Cull [_Cull]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _PSX_SPECULAR
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 uvAffine   : TEXCOORD1; // (uv*w, w)
                half3  lighting   : TEXCOORD2;
                half3  specular   : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Difuso + especular de una luz, calculado por vertice
            void AccumulateLight(Light l, float3 nWS, float3 viewDirWS,
                                 inout half3 diffuse, inout half3 spec)
            {
                half atten = l.distanceAttenuation * l.shadowAttenuation;
                half3 radiance = l.color * atten;
                diffuse += radiance * saturate(dot(nWS, l.direction));
                #if defined(_PSX_SPECULAR)
                    float3 h = normalize(l.direction + viewDirWS);
                    float shininess = exp2(_Smoothness * 10.0 + 1.0);
                    spec += radiance * pow(saturate(dot(nWS, h)), shininess);
                #endif
            }

            Varyings vert(Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                float w = vp.positionCS.w;
                o.positionCS = PSXSnap(vp.positionCS);

                float2 uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                // Texture swim: la textura se arrastra segun cuanto salto el vertice al snapear
                float2 snapDelta = o.positionCS.xy / o.positionCS.w - vp.positionCS.xy / vp.positionCS.w;
                uv += snapDelta * _SwimStrength;
                o.uv = uv;
                // Truco affine: (uv*w)/w interpolado = interpolacion lineal en pantalla
                o.uvAffine = float3(uv * w, w);

                // Iluminacion Gouraud (por vertice)
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(vp.positionWS);

                half3 diffuse = 0;
                half3 spec = 0;

                Light mainLight = GetMainLight();
                AccumulateLight(mainLight, nWS, viewDirWS, diffuse, spec);

                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX) || USE_FORWARD_PLUS
                    InputData inputData = (InputData)0;
                    inputData.positionWS = vp.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(o.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light l = GetAdditionalLight(lightIndex, vp.positionWS);
                        AccumulateLight(l, nWS, viewDirWS, diffuse, spec);
                    LIGHT_LOOP_END
                #endif

                half3 ambient = SampleSH(nWS) * _AmbientBoost;
                o.lighting = diffuse + ambient;
                o.specular = spec * _SpecColor.rgb;

                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.color = IN.color;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Camera clip: en fragmento, positionCS.w = distancia de vista
                if (_ClipDistance > 0.0)
                    clip(IN.positionCS.w - _ClipDistance);

                float2 uvAffine = IN.uvAffine.xy / IN.uvAffine.z;
                float2 uv = PSXTextureUV(lerp(IN.uv, uvAffine, _AffineStrength));

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_PSXPointRepeat, uv) * _BaseColor * IN.color;
                #if defined(_ALPHATEST_ON)
                    clip(tex.a - _Cutoff);
                #endif

                half3 col = tex.rgb * IN.lighting;
                #if defined(_PSX_SPECULAR)
                    col += IN.specular;
                #endif

                // Color precision
                half levels = exp2(_ColorBits) - 1.0;
                col = floor(saturate(col) * levels + 0.5) / levels;

                col = MixFog(col, IN.fogFactor);
                return half4(col, tex.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = PSXSnap(TransformObjectToHClip(IN.positionOS.xyz));
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }

            half frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                if (_ClipDistance > 0.0)
                    clip(IN.positionCS.w - _ClipDistance);
                #if defined(_ALPHATEST_ON)
                    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_PSXPointRepeat, PSXTextureUV(IN.uv)).a * _BaseColor.a;
                    clip(a - _Cutoff);
                #endif
                return IN.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
