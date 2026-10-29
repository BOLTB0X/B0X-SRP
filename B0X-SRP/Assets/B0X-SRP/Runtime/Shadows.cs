using UnityEngine;
using UnityEngine.Rendering;

public sealed class Shadows
{
    const int shadowMapSize = 512;
    const string bufferName = "Shadows";

    static readonly int shadowMapId = Shader.PropertyToID("_B0XDirectionalShadowMap");
    static readonly int shadowMatrixId = Shader.PropertyToID("_B0XDirectionalShadowMatrix");
    static readonly int shadowStrengthId = Shader.PropertyToID("_B0XDirectionalShadowStrength");

    readonly CommandBuffer buffer = new CommandBuffer { name = bufferName };
    bool hasShadowMap;

    public void Render(
        ScriptableRenderContext context,
        CullingResults cullingResults,
        int visibleLightIndex,
        VisibleLight visibleLight
    )
    {
        Light light = visibleLight.light;
        hasShadowMap = light != null && light.shadows != LightShadows.None &&
            light.shadowStrength > 0f &&
            cullingResults.GetShadowCasterBounds(visibleLightIndex, out _);

        if (!hasShadowMap)
        {
            buffer.SetGlobalFloat(shadowStrengthId, 0f);
            Execute(context);
            return;
        }

        if (!cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
            visibleLightIndex, 0, 1, Vector3.zero, shadowMapSize, light.shadowNearPlane,
            out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix,
            out ShadowSplitData splitData))
        {
            hasShadowMap = false;
            buffer.SetGlobalFloat(shadowStrengthId, 0f);
            Execute(context);
            return;
        }

        buffer.GetTemporaryRT(
            shadowMapId, shadowMapSize, shadowMapSize, 16,
            FilterMode.Bilinear, RenderTextureFormat.Shadowmap
        );
        buffer.SetRenderTarget(shadowMapId);
        buffer.SetViewport(new Rect(0f, 0f, shadowMapSize, shadowMapSize));
        buffer.ClearRenderTarget(
            true, false, Color.clear, SystemInfo.usesReversedZBuffer ? 0f : 1f
        );
        buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
        buffer.SetGlobalTexture(shadowMapId, shadowMapId);
        buffer.SetGlobalMatrix(
            shadowMatrixId, ConvertToShadowMatrix(projectionMatrix, viewMatrix)
        );
        buffer.SetGlobalFloat(shadowStrengthId, light.shadowStrength);
        Execute(context);

        var settings = new ShadowDrawingSettings(cullingResults, visibleLightIndex);
#pragma warning disable CS0618
        settings.splitData = splitData;
#pragma warning restore CS0618
        RendererList shadowRenderers = context.CreateShadowRendererList(ref settings);
        buffer.DrawRendererList(shadowRenderers);
        Execute(context);
    }

    public void Cleanup(ScriptableRenderContext context)
    {
        if (!hasShadowMap)
            return;

        buffer.ReleaseTemporaryRT(shadowMapId);
        Execute(context);
        hasShadowMap = false;
    }

    static Matrix4x4 ConvertToShadowMatrix(Matrix4x4 projection, Matrix4x4 view)
    {
        Matrix4x4 scale = Matrix4x4.identity;
        scale.m00 = 0.5f;
        scale.m11 = 0.5f;
        scale.m03 = 0.5f;
        scale.m13 = 0.5f;
        scale.m22 = SystemInfo.usesReversedZBuffer ? -0.5f : 0.5f;
        scale.m23 = 0.5f;
        return scale * projection * view;
    }

    void Execute(ScriptableRenderContext context)
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }
}
