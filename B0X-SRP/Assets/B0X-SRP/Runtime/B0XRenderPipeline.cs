using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class B0XRenderPipeline : RenderPipeline
{
    private CameraRenderer renderer = new CameraRenderer();

    protected override void Render(
        ScriptableRenderContext context,
        List<Camera> cameras)
    {
        foreach (Camera camera in cameras)
        {
            renderer.Render(context, camera);
        }
    }
}