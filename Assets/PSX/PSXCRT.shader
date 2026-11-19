// Efecto CRT: curvatura, aberracion cromatica, scanlines, ruido y vineta.
Shader "Hidden/PSX/CRT"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "PSXCRT"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // x = curvatura, y = vineta, z = scanlines, w = ruido
            float4 _CRTParams1;
            // x = aberracion cromatica, y = numero de scanlines
            float4 _CRTParams2;

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 cc = uv * 2.0 - 1.0;

                // Curvatura de pantalla
                cc *= 1.0 + dot(cc.yx * cc.yx, float2(_CRTParams1.x, _CRTParams1.x));
                float2 uvC = cc * 0.5 + 0.5;

                // Fuera de la pantalla curvada: negro
                if (uvC.x < 0.0 || uvC.x > 1.0 || uvC.y < 0.0 || uvC.y > 1.0)
                    return half4(0, 0, 0, 1);

                // Aberracion cromatica (crece hacia los bordes)
                float2 dir = cc * _CRTParams2.x;
                float r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uvC + dir).r;
                float g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uvC).g;
                float b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uvC - dir).b;
                float3 col = float3(r, g, b);

                // Scanlines
                float s = sin(uvC.y * _CRTParams2.y * 6.2831853) * 0.5 + 0.5;
                col *= lerp(1.0, s, _CRTParams1.z);

                // Ruido animado
                float n = Hash(uvC * _ScreenParams.xy + _TimeParameters.x) - 0.5;
                col += n * _CRTParams1.w;

                // Vineta
                float v = 1.0 - dot(cc, cc) * 0.5 * _CRTParams1.y;
                col *= saturate(v);

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
}
