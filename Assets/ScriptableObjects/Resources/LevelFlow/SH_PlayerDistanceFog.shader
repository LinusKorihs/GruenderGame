Shader "Hidden/Axolite/PlayerDistanceFog"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0, 0, 0, 1)
        _FogTarget ("Fog Target", Vector) = (0, 0, 0, 1)
        _FogVisibleRadius ("Visible Radius", Float) = 14
        _FogFadeRadius ("Fade Radius", Float) = 12
        _FogVisibleAlpha ("Visible Alpha", Range(0, 1)) = 0
        _FogUnexploredAlpha ("Unexplored Alpha", Range(0, 1)) = 0.72
        _FogScreenRadius ("Screen Fallback Radius", Range(0, 1)) = 0.28
        _FogScreenSoftness ("Screen Fallback Softness", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Player Distance Fog"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float4 _FogTarget;
                float _FogVisibleRadius;
                float _FogFadeRadius;
                float _FogVisibleAlpha;
                float _FogUnexploredAlpha;
                float _FogScreenRadius;
                float _FogScreenSoftness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                real depth = SampleSceneDepth(uv);
                float alpha = _FogUnexploredAlpha;

                if (depth > 0.0001 && depth < 0.9999)
                {
                    float3 worldPosition = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                    float distanceToTarget = distance(worldPosition.xz, _FogTarget.xz);
                    float fadeEnd = max(_FogVisibleRadius + _FogFadeRadius, _FogVisibleRadius + 0.001);
                    float fogT = smoothstep(_FogVisibleRadius, fadeEnd, distanceToTarget);
                    alpha = lerp(_FogVisibleAlpha, _FogUnexploredAlpha, fogT);
                }
                else
                {
                    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    float2 centeredUv = float2((uv.x - 0.5) * aspect, uv.y - 0.5);
                    float fallbackT = smoothstep(_FogScreenRadius, _FogScreenRadius + _FogScreenSoftness, length(centeredUv));
                    alpha = lerp(_FogVisibleAlpha, _FogUnexploredAlpha, fallbackT);
                }

                return half4(_FogColor.rgb, alpha * _FogColor.a);
            }
            ENDHLSL
        }
    }
}
