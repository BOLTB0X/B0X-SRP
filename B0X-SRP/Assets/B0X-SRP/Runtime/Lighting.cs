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
		ambientColorId = Shader.PropertyToID("_B0XAmbientColor"),
		timeId = Shader.PropertyToID("_B0XTime");

	static Vector4[]
		dirLightColors = new Vector4[maxDirLightCount],
		dirLightDirections = new Vector4[maxDirLightCount];

	readonly Shadows shadows = new Shadows();

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
		SetupLights(context);
		buffer.EndSample(bufferName);
		context.ExecuteCommandBuffer(buffer);
		buffer.Clear();
	}

	public void Cleanup (ScriptableRenderContext context) => shadows.Cleanup(context);

	void SetupLights (ScriptableRenderContext context)
    {
		NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
		int dirLightCount = 0;
		int mainDirectionalLightIndex = -1;
		VisibleLight mainDirectionalLight = default;
		for (int i = 0; i < visibleLights.Length; i++)
        {
			VisibleLight visibleLight = visibleLights[i];
			if (visibleLight.lightType == LightType.Directional)
            {
				mainDirectionalLightIndex = i;
				mainDirectionalLight = visibleLight;
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
		float time = Time.time;
		buffer.SetGlobalVector(timeId, new Vector4(time * 0.05f, time, time * 2f, time * 3f));
		context.ExecuteCommandBuffer(buffer);
		buffer.Clear();

		if (mainDirectionalLightIndex >= 0)
			shadows.Render(context, cullingResults, mainDirectionalLightIndex, mainDirectionalLight);
	}

	void SetupDirectionalLight (int index, ref VisibleLight visibleLight)
    {
		dirLightColors[index] = visibleLight.finalColor;
		dirLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
	}
}
