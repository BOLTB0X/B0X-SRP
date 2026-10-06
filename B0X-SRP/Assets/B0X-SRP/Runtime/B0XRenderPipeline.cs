using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class B0XRenderPipeline : RenderPipeline
{
    private static readonly ShaderTagId[] ShaderTagIds =
    {
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly")
    };

    protected override void Render(
        ScriptableRenderContext context,
        List<Camera> cameras)
    {
        foreach (Camera camera in cameras)
        {
            RenderCamera(context, camera);
        }

        context.Submit();
    }

    private void RenderCamera(
        ScriptableRenderContext context,
        Camera camera)
    {
        if (!camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParameters))
        {
            return;
        }

        CullingResults cullingResults = context.Cull(ref cullingParameters);

        context.SetupCameraProperties(camera);

        CommandBuffer commandBuffer = CommandBufferPool.Get("B0X-SRP Clear");
        bool clearColor = camera.clearFlags == CameraClearFlags.Skybox
            || camera.clearFlags == CameraClearFlags.SolidColor;
        bool clearDepth = camera.clearFlags != CameraClearFlags.Nothing;
        commandBuffer.ClearRenderTarget(clearDepth, clearColor, camera.backgroundColor);
        context.ExecuteCommandBuffer(commandBuffer);
        CommandBufferPool.Release(commandBuffer);

        DrawRenderers(
            context,
            camera,
            cullingResults,
            RenderQueueRange.opaque,
            SortingCriteria.CommonOpaque
        );

        if (camera.clearFlags == CameraClearFlags.Skybox)
        {
            context.DrawSkybox(camera);
        }

        DrawRenderers(
            context,
            camera,
            cullingResults,
            RenderQueueRange.transparent,
            SortingCriteria.CommonTransparent
        );
    }

    private static void DrawRenderers(
        ScriptableRenderContext context,
        Camera camera,
        CullingResults cullingResults,
        RenderQueueRange renderQueueRange,
        SortingCriteria sortingCriteria)
    {
        DrawingSettings drawingSettings = new DrawingSettings(
            ShaderTagIds[0],
            new SortingSettings(camera) { criteria = sortingCriteria }
        );

        for (int i = 1; i < ShaderTagIds.Length; i++)
        {
            drawingSettings.SetShaderPassName(i, ShaderTagIds[i]);
        }

        FilteringSettings filteringSettings = new FilteringSettings(renderQueueRange);
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
    }
}