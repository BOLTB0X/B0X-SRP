#ifndef B0X_SHADOW_CASTER_PASS_INCLUDED
#define B0X_SHADOW_CASTER_PASS_INCLUDED

#include "../ShaderLibrary/Common.hlsl"
#include "../ShaderLibrary/Light.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseColor;
    float _Cutoff;
CBUFFER_END

struct ShadowCasterAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
};

struct ShadowCasterVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

ShadowCasterVaryings ShadowCasterVertex(ShadowCasterAttributes input)
{
    ShadowCasterVaryings output;
    float3 positionWS = TransformObjectToWorld(input.positionOS);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    positionWS += normalWS * 0.002;
    output.positionCS = TransformWorldToHClip(positionWS);
    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    return output;
}

float4 ShadowCasterFragment(ShadowCasterVaryings input) : SV_TARGET
{
    #if defined(_CLIPPING)
        float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
        clip(alpha - _Cutoff);
    #endif
    return 0.0;
}

#endif
