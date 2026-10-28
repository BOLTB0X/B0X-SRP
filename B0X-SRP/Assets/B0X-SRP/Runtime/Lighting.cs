using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class Lighting
{

	const string bufferName = "Lighting";

	const int maxDirLightCount = 1;

	static int
		dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
		dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
		dirLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections"),
		ambientColorId = Shader.PropertyToID("_B0XAmbientColor");

	static Vector4[]
		dirLightColors = new Vector4[maxDirLightCount],
		dirLightDirections = new Vector4[maxDirLightCount];

	CommandBuffer buffer = new CommandBuffer {
		name = bufferName
	};

	CullingResults cullingResults;

	public void Setup (
		ScriptableRenderContext context, CullingResults cullingResults
	)
    {
		this.cullingResults = cullingResults;
		buffer.BeginSample(bufferName);
		SetupLights();
		buffer.EndSample(bufferName);
		context.ExecuteCommandBuffer(buffer);
		buffer.Clear();
	}

	void SetupLights ()
    {
		NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
		int dirLightCount = 0;
		for (int i = 0; i < visibleLights.Length; i++)
        {
			VisibleLight visibleLight = visibleLights[i];
			if (visibleLight.lightType == LightType.Directional)
            {
				SetupDirectionalLight(dirLightCount++, ref visibleLight);
				if (dirLightCount >= maxDirLightCount) break;
			}
		}

		buffer.SetGlobalInt(dirLightCountId, dirLightCount);
		buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
		buffer.SetGlobalVectorArray(dirLightDirectionsId, dirLightDirections);
		buffer.SetGlobalVector(
			ambientColorId,
			RenderSettings.ambientLight * RenderSettings.ambientIntensity
		);
	}

	void SetupDirectionalLight (int index, ref VisibleLight visibleLight)
    {
		dirLightColors[index] = visibleLight.finalColor;
		dirLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
	}
}
