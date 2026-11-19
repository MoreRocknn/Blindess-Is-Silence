// Post-proceso PSX: pixelado + reduccion de color + dithering ordenado (Bayer 4x4).
// Usado por PSXPostProcessFeature (Unity 6, URP, Render Graph).
Shader "Hidden/PSX/PostProcess"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "PSXPostProcess"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // x = resolucion vertical (0 = sin pixelado), y = bits por canal, z = fuerza dither
            float4 _PSXParams;

            // Matriz Bayer 4x4 normalizada a [0,1)
            static const float BAYER4[16] =
            {
                 0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                 3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // ---- Pixelado ----
                // Resolucion simulada: alto fijo, ancho segun aspect de pantalla
                float2 pixelCoord;
                if (_PSXParams.x > 0.0)
                {
                    float aspect = _ScreenParams.x / _ScreenParams.y;
                    float2 res = float2(round(_PSXParams.x * aspect), _PSXParams.x);
                    pixelCoord = floor(uv * res);
                    uv = (pixelCoord + 0.5) / res; // centro del "pixel grande"
                }
                else
                {
                    pixelCoord = floor(uv * _ScreenParams.xy);
                }

                float3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;

                // ---- Reduccion de color + dithering ----
                float levels = exp2(_PSXParams.y) - 1.0; // 5 bits -> 31 niveles
                uint2 p = (uint2)pixelCoord & 3u;
                float bayer = BAYER4[p.y * 4u + p.x];

                // Umbral centrado: con dither = 0 equivale a redondeo normal
                float3 v = col * levels + 0.5 + (bayer - 0.5) * _PSXParams.z;
                col = floor(v) / levels;

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
