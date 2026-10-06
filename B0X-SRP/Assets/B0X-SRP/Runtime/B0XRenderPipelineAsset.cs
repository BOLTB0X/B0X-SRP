using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(
    fileName = "B0XRenderPipelineAsset",
    menuName = "B0X-SRP/Render Pipeline Asset"
)]
public class B0XRenderPipelineAsset : RenderPipelineAsset
{
    protected override RenderPipeline CreatePipeline()
    {
        return new B0XRenderPipeline();
    }
}