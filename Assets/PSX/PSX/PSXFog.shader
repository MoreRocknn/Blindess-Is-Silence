// Niebla en espacio de pantalla estilo PSX (lineal, con bandas opcionales).
// Requiere "Depth Texture" activado en el URP Asset.
Shader "Hidden/PSX/Fog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "PSXFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _PSXFogColor;
            // x = inicio, y = fin, z = intensidad, w = pasos de banda (0 = continuo)
            float4 _PSXFogParams;
            // x = afecta al cielo (0/1)
            float4 _PSXFogParams2;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                float rawDepth = SampleSceneDepth(uv);

                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 0.0;
                #else
                    bool isSky = rawDepth >= 1.0;
                #endif

                float fog;
                if (isSky)
                {
                    fog = _PSXFogParams2.x;
                }
                else
                {
                    float dist = LinearEyeDepth(rawDepth, _ZBufferParams);
                    fog = saturate((dist - _PSXFogParams.x) / max(_PSXFogParams.y - _PSXFogParams.x, 0.0001));
                }

                // Bandas de niebla (efecto escalonado PS1)
                if (_PSXFogParams.w > 0.0)
                    fog = floor(fog * _PSXFogParams.w + 0.5) / _PSXFogParams.w;

                col.rgb = lerp(col.rgb, _PSXFogColor.rgb, fog * _PSXFogParams.z);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
