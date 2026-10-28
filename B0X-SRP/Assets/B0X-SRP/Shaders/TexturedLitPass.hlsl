#ifndef B0X_TEXTURED_LIT_PASS_INCLUDED
#define B0X_TEXTURED_LIT_PASS_INCLUDED

#include "../ShaderLibrary/Common.hlsl"
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"
#include "../ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);
TEXTURE2D(_MetallicMap);
SAMPLER(sampler_MetallicMap);
TEXTURE2D(_RoughnessMap);
SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_OcclusionMap);
SAMPLER(sampler_OcclusionMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _NormalMap_ST;
    float4 _MetallicMap_ST;
    float4 _RoughnessMap_ST;
    float4 _OcclusionMap_ST;
    float4 _BaseColor;
    float _NormalScale;
    float _Metallic;
    float _Smoothness;
    float _OcclusionStrength;
    float _Cutoff;
CBUFFER_END

float4 _B0XAmbientColor;

struct LitPassAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct LitPassVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;
    float2 uv : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

LitPassVaryings LitPassVertex (LitPassAttributes input)
{
    LitPassVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS = TransformWorldToHClip(positionWS);
    output.positionWS = positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.tangentWS = float4(
        TransformObjectToWorldDir(input.tangentOS.xyz),
        input.tangentOS.w * unity_WorldTransformParams.w
    );
    output.uv = input.uv;
    return output;
}

float4 LitPassFragment (LitPassVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);

    float2 baseUV = TRANSFORM_TEX(input.uv, _BaseMap);
    float2 normalUV = TRANSFORM_TEX(input.uv, _NormalMap);
    float2 metallicUV = TRANSFORM_TEX(input.uv, _MetallicMap);
    float2 roughnessUV = TRANSFORM_TEX(input.uv, _RoughnessMap);
    float2 occlusionUV = TRANSFORM_TEX(input.uv, _OcclusionMap);

    float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV) * _BaseColor;
    #if defined(_CLIPPING)
        clip(baseSample.a - _Cutoff);
    #endif

    float3 normalWS = normalize(input.normalWS);
    float3 tangentWS = normalize(input.tangentWS.xyz);
    float3 bitangentWS = normalize(cross(normalWS, tangentWS)) * input.tangentWS.w;
    float3 normalTS = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV).xyz * 2.0 - 1.0;
    normalTS.xy *= _NormalScale;
    normalTS = normalize(normalTS);
    normalWS = normalize(
        tangentWS * normalTS.x + bitangentWS * normalTS.y + normalWS * normalTS.z
    );

    Surface surface;
    surface.normal = normalWS;
    surface.viewDirection = SafeNormalize(_WorldSpaceCameraPos - input.positionWS);
    surface.color = baseSample.rgb;
    surface.alpha = baseSample.a;
    surface.metallic = saturate(
        SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, metallicUV).r * _Metallic
    );
    float roughness = saturate(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, roughnessUV).r);
    surface.smoothness = saturate((1.0 - roughness) * _Smoothness);

    BRDF brdf = GetBRDF(surface);
    float3 color = GetLighting(surface, brdf);

    float aoSample = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, occlusionUV).r;
    float ao = lerp(1.0, aoSample, _OcclusionStrength);
    color += surface.color * _B0XAmbientColor.rgb *
        OneMinusReflectivity(surface.metallic) * ao;

    #if defined(_PREMULTIPLY_ALPHA)
        color *= surface.alpha;
    #endif

    return float4(color, surface.alpha);
}

#endif
