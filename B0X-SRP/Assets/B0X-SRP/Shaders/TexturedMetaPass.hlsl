#ifndef B0X_TEXTURED_META_PASS_INCLUDED
#define B0X_TEXTURED_META_PASS_INCLUDED

#include "../ShaderLibrary/Common.hlsl"
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Shadows.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_MetallicMap); SAMPLER(sampler_MetallicMap);
TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

float4 _BaseMap_ST, _BaseColor;
float _Metallic, _Smoothness, _Cutoff;
float4 _EmissionColor;
float4 _EmissionMap_ST;
bool4 unity_MetaFragmentControl;
float unity_OneOverOutputBoost, unity_MaxOutputValue;

struct TexturedMetaAttributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float2 lightMapUV : TEXCOORD1; };
struct TexturedMetaVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

TexturedMetaVaryings TexturedMetaVertex (TexturedMetaAttributes input)
{
    TexturedMetaVaryings output;
    input.positionOS.xy = input.lightMapUV * unity_LightmapST.xy + unity_LightmapST.zw;
    input.positionOS.z = input.positionOS.z > 0.0 ? FLT_MIN : 0.0;
    output.positionCS = TransformWorldToHClip(input.positionOS);
    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    return output;
}

float4 TexturedMetaFragment (TexturedMetaVaryings input) : SV_TARGET
{
    float4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
    #if defined(_CLIPPING)
        clip(base.a - _Cutoff);
    #endif
    Surface surface;
    ZERO_INITIALIZE(Surface, surface);
    surface.color = base.rgb;
    surface.metallic = saturate(SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, input.uv).r * _Metallic);
    surface.smoothness = saturate((1.0 - SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, input.uv).r) * _Smoothness);
    BRDF brdf = GetBRDF(surface);
    float4 meta = 0.0;
    if (unity_MetaFragmentControl.x) {
        meta = float4(brdf.diffuse + brdf.specular * brdf.roughness * 0.5, 1.0);
        meta.rgb = min(PositivePow(meta.rgb, unity_OneOverOutputBoost), unity_MaxOutputValue);
    } else if (unity_MetaFragmentControl.y) {
        meta = SAMPLE_TEXTURE2D(
            _EmissionMap, sampler_EmissionMap,
            input.uv * _EmissionMap_ST.xy + _EmissionMap_ST.zw
        ) * _EmissionColor;
    }
    return meta;
}
#endif
