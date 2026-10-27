Shader "B0X-SRP/Metallic"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MetallicMap ("Metallic Map", 2D) = "black" {}
        _Metallic ("Metallic", Range(0, 1)) = 1
        _RoughnessMap ("Roughness Map", 2D) = "white" {}
        _Roughness ("Roughness", Range(0.04, 1)) = 1
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Range(0, 2)) = 1
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

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetallicMap);
            SAMPLER(sampler_MetallicMap);
            TEXTURE2D(_RoughnessMap);
            SAMPLER(sampler_RoughnessMap);
            TEXTURE2D(_OcclusionMap);
            SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _MetallicMap_ST;
                float4 _RoughnessMap_ST;
                float4 _OcclusionMap_ST;
                float4 _NormalMap_ST;
                float4 _BaseColor;
                float _Metallic;
                float _Roughness;
                float _OcclusionStrength;
                float _NormalScale;
            CBUFFER_END

            float4 _B0XMainLightDirection;
            float4 _B0XMainLightColor;
            float4 _B0XAmbientColor;
            float4 _B0XPointLightPositions[4];
            float4 _B0XPointLightColors[4];
            int _B0XPointLightCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(
                    TransformObjectToWorldDir(input.tangentOS.xyz),
                    input.tangentOS.w * GetOddNegativeScale()
                );
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 metallicUV = TRANSFORM_TEX(input.uv, _MetallicMap);
                float2 roughnessUV = TRANSFORM_TEX(input.uv, _RoughnessMap);
                float2 occlusionUV = TRANSFORM_TEX(input.uv, _OcclusionMap);
                float2 normalUV = TRANSFORM_TEX(input.uv, _NormalMap);

                float2 baseUV = TRANSFORM_TEX(input.uv, _BaseMap);
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV);
                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                half metallic = SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, metallicUV).r * _Metallic;
                half roughness = saturate(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, roughnessUV).r * _Roughness);
                roughness = max(roughness, 0.04h);
                half aoSample = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, occlusionUV).r;
                half ao = lerp(1.0h, aoSample, _OcclusionStrength);

                float3 normal = normalize(input.normalWS);
                float3 tangent = normalize(input.tangentWS.xyz);
                float3 bitangent = normalize(cross(normal, tangent)) * input.tangentWS.w;
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV),
                    _NormalScale
                );
                normal = normalize(
                    tangent * normalTS.x + bitangent * normalTS.y + normal * normalTS.z
                );

                float3 lightDirection = normalize(_B0XMainLightDirection.xyz);
                float3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 halfDirection = normalize(lightDirection + viewDirection);
                half nDotL = saturate(dot(normal, lightDirection));
                half nDotV = saturate(dot(normal, viewDirection));
                half nDotH = saturate(dot(normal, halfDirection));
                half vDotH = saturate(dot(viewDirection, halfDirection));

                // Minimal Cook-Torrance GGX direct-light model (no shadows or IBL yet).
                half alpha = roughness * roughness;
                half alpha2 = alpha * alpha;
                half denominator = nDotH * nDotH * (alpha2 - 1.0h) + 1.0h;
                half distribution = alpha2 / max(PI * denominator * denominator, 1e-4h);
                half k = (roughness + 1.0h) * (roughness + 1.0h) * 0.125h;
                half geometryV = nDotV / max(nDotV * (1.0h - k) + k, 1e-4h);
                half geometryL = nDotL / max(nDotL * (1.0h - k) + k, 1e-4h);
                half3 f0 = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic);
                half3 fresnel = f0 + (1.0h - f0) * pow(1.0h - vDotH, 5.0h);
                half3 specular = distribution * geometryV * geometryL * fresnel /
                    max(4.0h * nDotV * nDotL, 1e-4h);
                half3 diffuse = (1.0h - fresnel) * (1.0h - metallic) * albedo / PI;

                half3 directLighting = (diffuse + specular) * _B0XMainLightColor.rgb * nDotL;

                [unroll]
                for (int lightIndex = 0; lightIndex < 4; lightIndex++)
                {
                    if (lightIndex >= _B0XPointLightCount)
                    {
                        break;
                    }

                    float3 toPointLight = _B0XPointLightPositions[lightIndex].xyz - input.positionWS;
                    float distanceSquared = max(dot(toPointLight, toPointLight), 1e-4);
                    float distanceToLight = sqrt(distanceSquared);
                    float3 pointLightDirection = toPointLight / distanceToLight;
                    float pointRange = max(_B0XPointLightPositions[lightIndex].w, 1e-3);
                    half rangeFade = saturate(1.0h - distanceToLight / pointRange);
                    half pointAttenuation = rangeFade * rangeFade / (1.0h + distanceSquared);
                    half pointNDotL = saturate(dot(normal, pointLightDirection));
                    float3 pointHalfDirection = normalize(pointLightDirection + viewDirection);
                    half pointNDotH = saturate(dot(normal, pointHalfDirection));
                    half pointVDotH = saturate(dot(viewDirection, pointHalfDirection));

                    half pointDenominator = pointNDotH * pointNDotH * (alpha2 - 1.0h) + 1.0h;
                    half pointDistribution = alpha2 /
                        max(PI * pointDenominator * pointDenominator, 1e-4h);
                    half pointGeometryL = pointNDotL /
                        max(pointNDotL * (1.0h - k) + k, 1e-4h);
                    half3 pointFresnel = f0 + (1.0h - f0) * pow(1.0h - pointVDotH, 5.0h);
                    half3 pointSpecular = pointDistribution * geometryV * pointGeometryL * pointFresnel /
                        max(4.0h * nDotV * pointNDotL, 1e-4h);
                    half3 pointDiffuse = (1.0h - pointFresnel) * (1.0h - metallic) * albedo / PI;
                    directLighting += (pointDiffuse + pointSpecular) *
                        _B0XPointLightColors[lightIndex].rgb * pointNDotL * pointAttenuation;
                }

                half3 ambientLighting = albedo * _B0XAmbientColor.rgb * (1.0h - metallic);
                half3 color = directLighting + ambientLighting * ao;
                return half4(color, baseSample.a * _BaseColor.a);
            }

            ENDHLSL
        }
    }
}
