Shader "Custom/Shaders/TexturedLit"
{   
	Properties
    {
		_BaseMap("Texture", 2D) = "white" {}
		_BaseColor("Color", Color) = (0.5, 0.5, 0.5, 1.0)
		_NormalMap("Normal Map", 2D) = "bump" {}
		_NormalScale("Normal Scale", Range(0, 2)) = 1
		_HeightMap("Height Map", 2D) = "gray" {}
		[Toggle(_PARALLAX_MAPPING)] _ParallaxMapping ("Use Height Map", Float) = 0
		_ParallaxStrength("Height Scale", Range(0, 0.1)) = 0.02
		_MetallicMap("Metallic Map", 2D) = "black" {}
		_Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
		[Toggle(_CLIPPING)] _Clipping ("Alpha Clipping", Float) = 0

		_Metallic ("Metallic", Range(0, 1)) = 0
		_Smoothness ("Smoothness", Range(0, 1)) = 0.5
		_RoughnessMap("Roughness Map", 2D) = "black" {}
		_OcclusionMap("Occlusion Map", 2D) = "white" {}
		_OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1

		[Toggle(_PREMULTIPLY_ALPHA)] _PremulAlpha ("Premultiply Alpha", Float) = 0

		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
		[Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
	}

	SubShader
    {
		Tags { "RenderPipeline" = "B0X-SRP" }
		Pass
        {
            Tags { "LightMode" = "CustomTexturedLit" }
			Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]

            HLSLPROGRAM
			#pragma target 3.5
			#pragma shader_feature _CLIPPING
			#pragma shader_feature _PREMULTIPLY_ALPHA
			#pragma shader_feature _PARALLAX_MAPPING
			#pragma multi_compile_instancing
			#pragma vertex LitPassVertex
			#pragma fragment LitPassFragment
			#include "TexturedLitPass.hlsl"
			ENDHLSL
		}

		Pass
		{
			Tags {
				"LightMode" = "ShadowCaster"
			}

			ColorMask 0

			HLSLPROGRAM
			#pragma target 3.5
			#pragma shader_feature _ _SHADOWS_CLIP _SHADOWS_DITHER
			#pragma multi_compile_instancing
			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment
			#include "ShadowCasterPass.hlsl"
			ENDHLSL
		}
	}

	CustomEditor "CustomShaderGUI"
}
