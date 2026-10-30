using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class B0XRenderPipeline : RenderPipeline
{
    private CameraRenderer renderer = new CameraRenderer();

    readonly bool useGPUInstancing;
    ShadowSettings shadowSettings;

    public B0XRenderPipeline (bool           useGPUInstancing,
                              bool           useSRPBatcher,
                              ShadowSettings shadowSettings)
    {
        this.useGPUInstancing = useGPUInstancing;
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
			renderer.Render(context, cameras[i], shadowSettings, useGPUInstancing);
		}
    }
}
