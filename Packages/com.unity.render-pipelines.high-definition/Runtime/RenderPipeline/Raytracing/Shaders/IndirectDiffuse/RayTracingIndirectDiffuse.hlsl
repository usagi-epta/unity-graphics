#define HAS_LIGHTLOOP

// Include and define the shader pass
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
#define SHADERPASS SHADERPASS_RAYTRACING

// HDRP include
#define SHADER_TARGET 50
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/HDStencilUsage.cs.hlsl"

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/PreIntegratedFGD/PreIntegratedFGD.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightEvaluation.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialEvaluation.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesGlobal.hlsl"

// Raytracing includes
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/ShaderVariablesRaytracingLightLoop.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RayTracingLightCluster.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RaytracingSampling.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/Common/RayTracingHelpers.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/ScreenSpaceLighting/ScreenSpaceLighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/Common/AtmosphericScatteringRayTracing.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/RayCountManager.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/RayTracingFallbackHierarchy.cs.hlsl"

// GI includes
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinGIUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"

// The target acceleration structure that we will evaluate the reflexion in
TEXTURE2D_X(_DepthTexture);
TYPED_TEXTURE2D_X(uint2, _StencilTexture);

// Output structure of the reflection raytrace shader
RW_TEXTURE2D_X(float4, _IndirectDiffuseTextureRW);

[shader("miss")]
void MissShaderIndirectDiffuse(inout RayIntersection rayIntersection : SV_RayPayload)
{
    float3 rayOrigin = WorldRayOrigin();
    float3 rayDirection = WorldRayDirection();

    float weight = 0.0f;

    // Cannot be done because we lack the multi compiles on ray trace shaders
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    // Try the APV if enabled
    if(_EnableProbeVolumes)
    {
        // Read from the APV
        float3 backBakeDiffuseLighting = 0.0;
        EvaluateAdaptiveProbeVolume(GetAbsolutePositionWS(rayOrigin),
                                    rayDirection,
                                    -rayDirection,
                                    0.0,
                                    0.0,
                                    _RaytracingAPVLayerMask,
                                    rayIntersection.color,
                                    backBakeDiffuseLighting);
        weight = 1.0;
    }
#endif

    // Try the reflection probes
    if ((RAYTRACINGFALLBACKHIERACHY_REFLECTION_PROBES & _RayTracingRayMissFallbackHierarchy) && weight < 1.0)
        rayIntersection.color += RayTraceReflectionProbes(rayOrigin, rayDirection, weight);

    if((RAYTRACINGFALLBACKHIERACHY_SKY & _RayTracingRayMissFallbackHierarchy) && weight < 1.0)
    {
        rayIntersection.color += SAMPLE_TEXTURECUBE_ARRAY_LOD(_SkyTexture, s_trilinear_clamp_sampler, rayDirection, 0.0, 0).xyz * (1.0 - weight);
        weight = 1.0f;
    }

    if (weight > 0.0)
        ApplyFogAttenuation(rayOrigin, rayDirection, rayIntersection.color);

    rayIntersection.t = _RaytracingRayMaxLength;
}

[shader("raygeneration")]
void RayGenIntegration()
{
    // Grab the dimensions of the current dispatch
    uint3 LaunchIndex = DispatchRaysIndex();
    uint3 LaunchDim = DispatchRaysDimensions();

    UNITY_XR_ASSIGN_VIEW_INDEX(LaunchIndex.z);

    // Compute the pixel coordinate to evaluate
    uint2 currentCoord = uint2(LaunchIndex.x, LaunchDim.y - LaunchIndex.y - 1);

    // Clear the output color texture
    _IndirectDiffuseTextureRW[COORD_TEXTURE2D_X(currentCoord)] = float4(0.0f, 0.0f, 0.0f, 0.0f);

    // Read the depth value
    float depthValue = LOAD_TEXTURE2D_X(_DepthTexture, currentCoord).x;
    uint stencilValue = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, currentCoord));
    // This point is part of the background or is unlit, we don't really care
    if (depthValue == UNITY_RAW_FAR_CLIP_VALUE || (stencilValue & STENCILUSAGE_IS_UNLIT) != 0)
    {
        _IndirectDiffuseTextureRW[COORD_TEXTURE2D_X(currentCoord)] = float4(0.0, 0.0, 0.0, 0.0f);
        return;
    }

    // Convert this to a world space position
    PositionInputs posInput = GetPositionInput(currentCoord, 1.0f/LaunchDim.xy, depthValue, UNITY_MATRIX_I_VP, GetWorldToViewMatrix(), 0);
    float distanceToCamera = length(posInput.positionWS);
    // The position is always in the right space.
    const float3 positionWS = posInput.positionWS;
    // Compute the incident vector on the surfaces
    const float3 viewWS = GetWorldSpaceNormalizeViewDir(positionWS);

    // Decode the world space normal
    NormalData normalData;
    DecodeFromNormalBuffer(currentCoord, normalData);

    // Variable that accumulate the radiance
    float3 finalColor = float3(0.0, 0.0, 0.0);

    // Count the number of rays that we will be traced
    if (_RayCountEnabled > 0)
    {
        uint3 counterIdx = uint3(currentCoord, INDEX_TEXTURE2D_ARRAY_X(RAYCOUNTVALUES_DIFFUSE_GI_FORWARD));
        _RayCountTexture[counterIdx] = _RayCountTexture[counterIdx] + _RaytracingNumSamples;
    }

    // Evaluate the ray bias
    float rayBias = EvaluateRayTracingBias(posInput.positionWS);

    // Loop through the samples and add their contribution
    for (int sampleIndex = 0; sampleIndex < _RaytracingNumSamples; ++sampleIndex)
    {
        // Compute the current sample index
        int globalSampleIndex = _RaytracingFrameIndex * _RaytracingNumSamples + sampleIndex;

        // Generate the new sample (follwing values of the sequence)
        float2 theSample;
        theSample.x = GetBNDSequenceSample(currentCoord, globalSampleIndex, 0);
        theSample.y = GetBNDSequenceSample(currentCoord, globalSampleIndex, 1);

        // Importance sample with a cosine lobe
        float3 sampleDir = SampleHemisphereCosine(theSample.x, theSample.y, normalData.normalWS);

        // Create the ray descriptor for this pixel
        RayDesc rayDescriptor;
        rayDescriptor.Origin = positionWS + normalData.normalWS * rayBias;
        rayDescriptor.Direction = sampleDir;
        rayDescriptor.TMin = 0.0f;
        rayDescriptor.TMax = _RaytracingRayMaxLength;

        // Adjust world-space position to match the RAS setup with XR single-pass and camera relative
        ApplyCameraRelativeXR(rayDescriptor.Origin);

        // Create and init the RayIntersection structure for this
        RayIntersection rayIntersection;
        rayIntersection.color = float3(0.0, 0.0, 0.0);
        rayIntersection.t = -1.0f;
        rayIntersection.remainingDepth = 1;
        rayIntersection.pixelCoord = currentCoord;
        rayIntersection.sampleIndex = globalSampleIndex;

        // In order to achieve filtering for the textures, we need to compute the spread angle of the pixel
        rayIntersection.cone.spreadAngle = _RaytracingPixelSpreadAngle + roughnessToSpreadAngle(1.0);
        rayIntersection.cone.width = distanceToCamera * _RaytracingPixelSpreadAngle;

        // Evaluate the ray intersection
        TraceRay(_RaytracingAccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, RAYTRACINGRENDERERFLAG_GLOBAL_ILLUMINATION, 0, 1, 0, rayDescriptor, rayIntersection);

        // Clamp the exposed color in HSV space
        rayIntersection.color = RayTracingHSVClamp(rayIntersection.color * GetCurrentExposureMultiplier(), _RaytracingIntensityClamp);

        // Contribute to the pixel
        finalColor += rayIntersection.color;
    }

    // Normalize the value
    finalColor *= 1.0f / _RaytracingNumSamples;

    // We store the sampled color and the weight that shall be used for it (1.0f)
    _IndirectDiffuseTextureRW[COORD_TEXTURE2D_X(currentCoord)] = float4(finalColor, 1.0f);
}
