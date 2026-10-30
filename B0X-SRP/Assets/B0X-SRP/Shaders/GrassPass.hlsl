#ifndef B0X_GRASS_PASS_INCLUDED
#define B0X_GRASS_PASS_INCLUDED

#include "../ShaderLibrary/Common.hlsl"
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Shadows.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"
#include "../ShaderLibrary/Lighting.hlsl"

//float4 _B0XTime;

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
    UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
    UNITY_DEFINE_INSTANCED_PROP(float, _Smoothness)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

float4 _B0XAmbientColor;

struct GrassPassAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct GrassPassVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

GrassPassVaryings GrassPassVertex (GrassPassAttributes input)
{
    GrassPassVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionOS = input.positionOS;

    float wave = sin(_Time.y * 3.0 + positionOS.x + positionOS.z) * 0.03;
    positionOS.x += wave * input.uv.y; 

    float3 positionWS = TransformObjectToWorld(positionOS);
    output.positionCS = TransformWorldToHClip(positionWS);
    output.positionWS = positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    
    float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseMap_ST);
    output.uv = input.uv * baseST.xy + baseST.zw;
    
    return output;
}

float4 GrassPassFragment (GrassPassVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);

    float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    float4 base = baseMap * baseColor;

    #if defined(_CLIPPING)
        float cutoff = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff);
        clip(base.a - cutoff);
    #endif

    Surface surface;
    surface.position = input.positionWS;
    surface.normal = normalize(input.normalWS);
    surface.viewDirection = SafeNormalize(_WorldSpaceCameraPos - input.positionWS);
    surface.color = base.rgb;
    surface.alpha = base.a;
    surface.metallic = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
    surface.smoothness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Smoothness);
    surface.depth = -TransformWorldToView(input.positionWS).z;
    surface.dither = InterleavedGradientNoise(input.positionCS.xy, 0);

    BRDF brdf = GetBRDF(surface);
    float3 color = GetLighting(surface, brdf);

    color += surface.color * _B0XAmbientColor.rgb * OneMinusReflectivity(surface.metallic);

    return float4(color, surface.alpha);
}

#endif
