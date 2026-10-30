using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class Lighting
{

	const string bufferName = "Lighting";

	const int maxDirLightCount = 2;
	const int maxPointLightCount = 4;

	static int
		dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
		dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
		dirLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections"),
		dirLightShadowDataId =
			Shader.PropertyToID("_DirectionalLightShadowData"),
		pointLightCountId = Shader.PropertyToID("_PointLightCount"),
		pointLightPositionsId = Shader.PropertyToID("_PointLightPositions"),
		pointLightColorsId = Shader.PropertyToID("_PointLightColors"),
		ambientColorId = Shader.PropertyToID("_B0XAmbientColor"),
		timeId = Shader.PropertyToID("_B0XTime");

	static Vector4[]
		dirLightColors = new Vector4[maxDirLightCount],
		dirLightDirections = new Vector4[maxDirLightCount],
		dirLightShadowData = new Vector4[maxDirLightCount],
		pointLightPositions = new Vector4[maxPointLightCount],
		pointLightColors = new Vector4[maxPointLightCount];

	CommandBuffer buffer = new CommandBuffer {
		name = bufferName
	};

	CullingResults cullingResults;
	Shadows shadows = new Shadows();

	public void Setup (ScriptableRenderContext context,
					   CullingResults          cullingResults,
					   ShadowSettings          shadowSettings
	)
    {
		this.cullingResults = cullingResults;
		buffer.BeginSample(bufferName);
		shadows.Setup(context, cullingResults, shadowSettings);
		SetupLights(context);
		shadows.Render();
		buffer.EndSample(bufferName);
		context.ExecuteCommandBuffer(buffer);
		buffer.Clear();
	}

	public void Cleanup ()
	{
		shadows.Cleanup();
	}

	void SetupLights (ScriptableRenderContext context)
    {
		NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
		int dirLightCount = 0;
		int pointLightCount = 0;
		for (int i = 0; i < visibleLights.Length; i++)
        {
			VisibleLight visibleLight = visibleLights[i];
			if (visibleLight.lightType == LightType.Directional)
            {
				if (dirLightCount < maxDirLightCount)
					SetupDirectionalLight(dirLightCount++, ref visibleLight);
			}
			else if (visibleLight.lightType == LightType.Point && pointLightCount < maxPointLightCount)
			{
				SetupPointLight(pointLightCount++, ref visibleLight);
			}
		}

		buffer.SetGlobalInt(dirLightCountId, dirLightCount);
		buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
		buffer.SetGlobalVectorArray(dirLightDirectionsId, dirLightDirections);
		buffer.SetGlobalVectorArray(dirLightShadowDataId, dirLightShadowData);
		buffer.SetGlobalInt(pointLightCountId, pointLightCount);
		buffer.SetGlobalVectorArray(pointLightPositionsId, pointLightPositions);
		buffer.SetGlobalVectorArray(pointLightColorsId, pointLightColors);
		buffer.SetGlobalVector(
			ambientColorId,
			RenderSettings.ambientLight * RenderSettings.ambientIntensity
		);
		float time = Time.time;
		buffer.SetGlobalVector(timeId, new Vector4(time * 0.05f, time, time * 2f, time * 3f));
		context.ExecuteCommandBuffer(buffer);
		buffer.Clear();
	}

	void SetupDirectionalLight (int index, ref VisibleLight visibleLight)
    {
		dirLightColors[index] = visibleLight.finalColor;
		dirLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
		dirLightShadowData[index] = shadows.ReserveDirectionalShadows(visibleLight.light, index);
	}

	void SetupPointLight (int index, ref VisibleLight visibleLight)
	{
		Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
		position.w = visibleLight.range;
		pointLightPositions[index] = position;
		pointLightColors[index] = visibleLight.finalColor;
	}
}
