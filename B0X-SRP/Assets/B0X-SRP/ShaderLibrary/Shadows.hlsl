#ifndef B0X_SHADOWS_INCLUDED
#define B0X_SHADOWS_INCLUDED

TEXTURE2D_SHADOW(_B0XDirectionalShadowMap);
SAMPLER_CMP(sampler_B0XDirectionalShadowMap);

float4x4 _B0XDirectionalShadowMatrix;
float _B0XDirectionalShadowStrength;

float GetDirectionalShadowAttenuation(float3 positionWS)
{
    if (_B0XDirectionalShadowStrength <= 0.0)
        return 1.0;

    float4 shadowPosition = mul(_B0XDirectionalShadowMatrix, float4(positionWS, 1.0));
    shadowPosition.xyz /= shadowPosition.w;
    if (shadowPosition.x < 0.0 || shadowPosition.x > 1.0 ||
        shadowPosition.y < 0.0 || shadowPosition.y > 1.0 ||
        shadowPosition.z < 0.0 || shadowPosition.z > 1.0)
        return 1.0;

    float visibility = SAMPLE_TEXTURE2D_SHADOW(
        _B0XDirectionalShadowMap,
        sampler_B0XDirectionalShadowMap,
        shadowPosition.xyz
    );
    return lerp(1.0, visibility, _B0XDirectionalShadowStrength);
}

#endif
