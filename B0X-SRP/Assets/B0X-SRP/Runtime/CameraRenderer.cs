using UnityEngine;
using UnityEngine.Rendering;

public class CameraRenderer
{
    private ScriptableRenderContext context;
    private Camera camera;

    private static readonly ShaderTagId[] ShaderTagIds =
    {
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly")
    };

    public void Render(
        ScriptableRenderContext context,
        Camera camera)
    {
        this.context = context;
        this.camera = camera;

        if (!camera.TryGetCullingParameters(
            out ScriptableCullingParameters cullingParameters))
        {
            return;
        }

        CullingResults cullingResults =
            context.Cull(ref cullingParameters);

        context.SetupCameraProperties(camera);

        Clear();

        DrawRenderers(
            cullingResults,
            RenderQueueRange.opaque,
            SortingCriteria.CommonOpaque
        );

        if (camera.clearFlags == CameraClearFlags.Skybox)
        {
            context.DrawSkybox(camera);
        }

        DrawRenderers(
            cullingResults,
            RenderQueueRange.transparent,
            SortingCriteria.CommonTransparent
        );

        context.Submit();
    }

    private void Clear()
    {
        CommandBuffer commandBuffer =
            CommandBufferPool.Get("B0X-SRP Clear");

        bool clearColor =
            camera.clearFlags == CameraClearFlags.Skybox
            || camera.clearFlags == CameraClearFlags.SolidColor;

        bool clearDepth =
            camera.clearFlags != CameraClearFlags.Nothing;

        commandBuffer.ClearRenderTarget(
            clearDepth,
            clearColor,
            camera.backgroundColor
        );

        context.ExecuteCommandBuffer(commandBuffer);

        CommandBufferPool.Release(commandBuffer);
    }

    private void DrawRenderers(
        CullingResults cullingResults,
        RenderQueueRange renderQueueRange,
        SortingCriteria sortingCriteria)
    {
        DrawingSettings drawingSettings =
            new DrawingSettings(
                ShaderTagIds[0],
                new SortingSettings(camera)
                {
                    criteria = sortingCriteria
                }
            );

        for (int i = 1; i < ShaderTagIds.Length; i++)
        {
            drawingSettings.SetShaderPassName(
                i,
                ShaderTagIds[i]
            );
        }

        FilteringSettings filteringSettings =
            new FilteringSettings(renderQueueRange);

        context.DrawRenderers(
            cullingResults,
            ref drawingSettings,
            ref filteringSettings
        );
    }
}