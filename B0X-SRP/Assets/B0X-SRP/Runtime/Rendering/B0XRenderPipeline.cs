using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class B0XRenderPipeline : RenderPipeline
{
    private CameraRenderer renderer = new CameraRenderer();

    readonly bool useGPUInstancing, useLightsPerObject;
    ShadowSettings shadowSettings;

    public B0XRenderPipeline (bool           useGPUInstancing,
                              bool           useSRPBatcher,
                              bool           useLightsPerObject,
                              ShadowSettings shadowSettings)
    {
        this.useGPUInstancing = useGPUInstancing;
        this.useLightsPerObject = useLightsPerObject;
        this.shadowSettings = shadowSettings;
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
		GraphicsSettings.lightsUseLinearIntensity = true;
    }

    protected override void Render (
		ScriptableRenderContext context, List<Camera> cameras
	)
    {
        for (int i = 0; i < cameras.Count; i++) {
			renderer.Render(context, cameras[i], shadowSettings, useLightsPerObject, useGPUInstancing);
		}
    }
}
