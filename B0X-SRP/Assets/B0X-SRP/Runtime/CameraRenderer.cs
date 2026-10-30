using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class CameraRenderer
{
    const string bufferName = "Render Camera";
    static readonly ShaderTagId[] shaderTagIds = {
        new ShaderTagId("CustomTexturedLit"),
        new ShaderTagId("CustomGrass"),
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly")
    };
    CommandBuffer buffer = new CommandBuffer { name = bufferName };
    ScriptableRenderContext context;
    Camera camera;
    CullingResults cullingResults;
    Lighting lighting = new Lighting();

#if UNITY_EDITOR
    string SampleName { get; set; }
#else
    const string SampleName = bufferName;
#endif

    partial void PrepareBuffer();
    partial void PrepareForSceneWindow();
    partial void DrawGizmos();
    partial void DrawUnsupportedShaders();

    public void Render (ScriptableRenderContext context,
                        Camera                  camera,
                        ShadowSettings          shadowSettings,
                        bool                    useGPUInstancing)
    {
        this.context = context;
        this.camera = camera;

        PrepareBuffer();
        PrepareForSceneWindow();
		if (!Cull(shadowSettings.maxDistance)) {
			return;
		}
		
		buffer.BeginSample(SampleName);
		ExecuteBuffer();
		lighting.Setup(context, cullingResults, shadowSettings);
		buffer.EndSample(SampleName);
		Setup();
		DrawVisibleGeometry(useGPUInstancing);
		DrawUnsupportedShaders();
		DrawGizmos();
		lighting.Cleanup();
		Submit();
    }

	bool Cull (float maxShadowDistance)
    {
		if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
			p.shadowDistance = Mathf.Min(maxShadowDistance, camera.farClipPlane);
			cullingResults = context.Cull(ref p);
			return true;
		}
		return false;
	}

    void Setup()
    {
        context.SetupCameraProperties(camera);
        CameraClearFlags flags = camera.clearFlags;
        //buffer.ClearRenderTarget(true, true, Color.clear);
        buffer.ClearRenderTarget(
			flags <= CameraClearFlags.Depth,
			flags == CameraClearFlags.Color,
			flags == CameraClearFlags.Color ?
				camera.backgroundColor.linear : Color.clear
		);
        buffer.BeginSample(SampleName);
        ExecuteBuffer();
    }

    void Submit()
    {
        buffer.EndSample(SampleName);
        ExecuteBuffer();
        context.Submit();
    }

    void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    void DrawVisibleGeometry(bool useGPUInstancing)
    {
		DrawRendererList(RenderQueueRange.opaque, SortingCriteria.CommonOpaque, useGPUInstancing);

		if (camera.clearFlags == CameraClearFlags.Skybox)
		{
			buffer.DrawRendererList(context.CreateSkyboxRendererList(camera));
			ExecuteBuffer();
		}

		DrawRendererList(RenderQueueRange.transparent, SortingCriteria.CommonTransparent, useGPUInstancing);
    }

	void DrawRendererList(RenderQueueRange queueRange, SortingCriteria sortingCriteria, bool useGPUInstancing)
	{
		var drawingSettings = new DrawingSettings(
			shaderTagIds[0], new SortingSettings(camera) { criteria = sortingCriteria }
		)
		{
			enableInstancing = useGPUInstancing
		};
		for (int i = 1; i < shaderTagIds.Length; i++)
		{
			drawingSettings.SetShaderPassName(i, shaderTagIds[i]);
		}

		var filteringSettings = new FilteringSettings(queueRange, camera.cullingMask);
		var rendererListParams = new RendererListParams(cullingResults, drawingSettings, filteringSettings);
		RendererList rendererList = context.CreateRendererList(ref rendererListParams);
		buffer.DrawRendererList(rendererList);
		ExecuteBuffer();
	}
}

#if UNITY_EDITOR
public partial class CameraRenderer
{
    static ShaderTagId[] legacyShaderTagIds = {
        new ShaderTagId("Always"),
        new ShaderTagId("ForwardBase"),
        new ShaderTagId("PrepassBase"),
        new ShaderTagId("Vertex"),
        new ShaderTagId("VertexLMRGBM"),
        new ShaderTagId("VertexLM")
    };

    static Material errorMaterial;

    partial void PrepareBuffer ()
    {
        Profiler.BeginSample("Editor Only");
        buffer.name = camera.name;
        SampleName = camera.name;
        Profiler.EndSample();
    }

    partial void PrepareForSceneWindow ()
    {
        if (camera.cameraType == CameraType.SceneView) {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
    }

    partial void DrawUnsupportedShaders()
    {
        if (errorMaterial == null) {
            errorMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
        }
		var drawingSettings = new DrawingSettings(legacyShaderTagIds[0], new SortingSettings(camera)) {
            overrideMaterial = errorMaterial
        };

        for (int i = 1; i < legacyShaderTagIds.Length; i++) {
            drawingSettings.SetShaderPassName(i, legacyShaderTagIds[i]);
        }

		var filteringSettings = FilteringSettings.defaultValue;
		var rendererListParams = new RendererListParams(cullingResults, drawingSettings, filteringSettings);
		RendererList rendererList = context.CreateRendererList(ref rendererListParams);
		buffer.DrawRendererList(rendererList);
		ExecuteBuffer();
    }

    partial void DrawGizmos()
    {
        if (Handles.ShouldRenderGizmos()) {
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
    }
}
#endif
