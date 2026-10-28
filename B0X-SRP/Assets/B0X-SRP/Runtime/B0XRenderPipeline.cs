using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class B0XRenderPipeline : RenderPipeline
{
    private CameraRenderer renderer = new CameraRenderer();

    readonly bool useGPUInstancing;

    public B0XRenderPipeline (bool useGPUInstancing, bool useSRPBatcher)
    {
        this.useGPUInstancing = useGPUInstancing;
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
    }

    protected override void Render (
		ScriptableRenderContext context, List<Camera> cameras
	)
    {
        for (int i = 0; i < cameras.Count; i++) {
			renderer.Render(context, cameras[i], useGPUInstancing);
		}
    }
}
