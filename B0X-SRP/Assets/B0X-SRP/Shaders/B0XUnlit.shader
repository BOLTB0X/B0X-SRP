Shader "B0X-SRP/Unlit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.5, 1.0, 0.2, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "B0X-SRP"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)

                float4 _BaseColor;

            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                OUT.positionCS =
                    TransformObjectToHClip(
                        IN.positionOS.xyz
                    );

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _BaseColor;
            }

            ENDHLSL
        }
    }
}