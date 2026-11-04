#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

#define MAX_DIRECTIONAL_LIGHT_COUNT 2
#define MAX_POINT_LIGHT_COUNT 64
#define MAX_SPOT_LIGHT_COUNT 64

CBUFFER_START(_CustomLight)
	int    _DirectionalLightCount;
	float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
	float4 _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHT_COUNT];
	float4 _DirectionalLightShadowData[MAX_DIRECTIONAL_LIGHT_COUNT];

	int    _PointLightCount;
	float4 _PointLightPositions[MAX_POINT_LIGHT_COUNT];
	float4 _PointLightColors[MAX_POINT_LIGHT_COUNT];
	int    _SpotLightCount;
	float4 _SpotLightPositions[MAX_SPOT_LIGHT_COUNT];
	float4 _SpotLightColors[MAX_SPOT_LIGHT_COUNT];
	float4 _SpotLightDirections[MAX_SPOT_LIGHT_COUNT];
	float4 _SpotLightSpotAngles[MAX_SPOT_LIGHT_COUNT];
CBUFFER_END

struct Light
{
	float3 color;
	float3 direction;
	float  attenuation;
};

int GetDirectionalLightCount () 
{
	return _DirectionalLightCount;
}

DirectionalShadowData GetDirectionalShadowData (int lightIndex, ShadowData shadowData)
{
	DirectionalShadowData data;
	data.strength =
		_DirectionalLightShadowData[lightIndex].x * shadowData.strength;
	data.tileIndex =
		_DirectionalLightShadowData[lightIndex].y + shadowData.cascadeIndex;
	data.normalBias = _DirectionalLightShadowData[lightIndex].z;
	return data;
}

Light GetDirectionalLight (int        index,
						   Surface    surfaceWS,
						   ShadowData shadowData)
{
	Light light;
	light.color = _DirectionalLightColors[index].rgb;
	light.direction = _DirectionalLightDirections[index].xyz;
	DirectionalShadowData dirShadowData = GetDirectionalShadowData(index, shadowData);
	light.attenuation = GetDirectionalShadowAttenuation(dirShadowData, shadowData, surfaceWS);
	return light;
}

int GetPointLightCount ()
{
	return _PointLightCount;
}

int GetSpotLightCount () { return _SpotLightCount; }

Light GetSpotLight (int index, Surface surfaceWS)
{
	Light light;
	float3 ray = _SpotLightPositions[index].xyz - surfaceWS.position;
	float distanceSqr = max(dot(ray, ray), 0.00001);
	light.direction = ray * rsqrt(distanceSqr);
	float rangeFade = saturate(1.0 - distanceSqr * _SpotLightPositions[index].w);
	float coneCos = dot(-light.direction, _SpotLightDirections[index].xyz);
	float coneFade = saturate((coneCos - _SpotLightDirections[index].w) / max(1.0 - _SpotLightDirections[index].w, 0.001));
	light.color = _SpotLightColors[index].rgb;
	light.attenuation = Square(rangeFade) * coneFade / distanceSqr;
	return light;
}

Light GetPointLight (int index, Surface surfaceWS)
{
	// Light light;
	// float3 toLight = _PointLightPositions[index].xyz - surfaceWS.position;
	// float distanceSquared = max(dot(toLight, toLight), 1e-4);
	// float distanceToLight = sqrt(distanceSquared);
	// float range = max(_PointLightPositions[index].w, 1e-3);
	// float rangeFade = saturate(1.0 - distanceToLight / range);
	// light.direction = toLight / distanceToLight;
	// light.color = _PointLightColors[index].rgb;
	// light.attenuation = rangeFade * rangeFade / distanceSquared;
	// return light;
	Light light;
	light.color = _PointLightColors[index].rgb;
	float3 ray = _PointLightPositions[index].xyz - surfaceWS.position;
	light.direction = normalize(ray);
	float distanceSqr = max(dot(ray, ray), 0.00001);
	float rangeAttenuation = Square(saturate(1.0 - distanceSqr * _PointLightPositions[index].w));
	light.attenuation = rangeAttenuation / distanceSqr;
	return light;
}

#endif
