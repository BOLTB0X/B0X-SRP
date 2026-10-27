using UnityEngine;
using UnityEngine.Rendering;

public class CameraRenderer
{
    private const int MaxPointLights = 4;

    private ScriptableRenderContext context;
    private Camera camera;
    private readonly Vector4[] pointLightPositions = new Vector4[MaxPointLights];
    private readonly Vector4[] pointLightColors = new Vector4[MaxPointLights];

    private static readonly int MainLightDirectionId =
        Shader.PropertyToID("_B0XMainLightDirection");
    private static readonly int MainLightColorId =
        Shader.PropertyToID("_B0XMainLightColor");
    private static readonly int AmbientColorId =
        Shader.PropertyToID("_B0XAmbientColor");
    private static readonly int PointLightPositionsId =
        Shader.PropertyToID("_B0XPointLightPositions");
    private static readonly int PointLightColorsId =
        Shader.PropertyToID("_B0XPointLightColors");
    private static readonly int PointLightCountId =
        Shader.PropertyToID("_B0XPointLightCount");

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

        SetupLighting(cullingResults);

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

    private void SetupLighting(CullingResults cullingResults)
    {
        Vector4 mainLightDirection = new Vector4(0f, 1f, 0f, 0f);
        Color mainLightColor = Color.black;
        bool foundMainLight = false;
        int pointLightCount = 0;

        for (int i = 0; i < MaxPointLights; i++)
        {
            pointLightPositions[i] = Vector4.zero;
            pointLightColors[i] = Vector4.zero;
        }

        var visibleLights = cullingResults.visibleLights;
        for (int i = 0; i < visibleLights.Length; i++)
        {
            VisibleLight visibleLight = visibleLights[i];
            if (visibleLight.lightType != LightType.Directional)
            {
                if (visibleLight.lightType == LightType.Point &&
                    pointLightCount < MaxPointLights)
                {
                    Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
                    pointLightPositions[pointLightCount] = new Vector4(
                        position.x,
                        position.y,
                        position.z,
                        visibleLight.range
                    );
                    pointLightColors[pointLightCount] = visibleLight.finalColor;
                    pointLightCount++;
                }
                continue;
            }

            Vector4 forward = visibleLight.localToWorldMatrix.GetColumn(2);
            mainLightDirection = new Vector4(-forward.x, -forward.y, -forward.z, 0f);
            mainLightColor = visibleLight.finalColor;
            foundMainLight = true;
            break;
        }

        if (!foundMainLight)
        {
            mainLightColor = Color.black;
        }

        Color ambientColor = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
        CommandBuffer commandBuffer = CommandBufferPool.Get("B0X-SRP Lighting");
        commandBuffer.SetGlobalVector(MainLightDirectionId, mainLightDirection);
        commandBuffer.SetGlobalVector(MainLightColorId, mainLightColor);
        commandBuffer.SetGlobalVector(AmbientColorId, ambientColor);
        commandBuffer.SetGlobalVectorArray(PointLightPositionsId, pointLightPositions);
        commandBuffer.SetGlobalVectorArray(PointLightColorsId, pointLightColors);
        commandBuffer.SetGlobalInt(PointLightCountId, pointLightCount);
        context.ExecuteCommandBuffer(commandBuffer);
        CommandBufferPool.Release(commandBuffer);
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
