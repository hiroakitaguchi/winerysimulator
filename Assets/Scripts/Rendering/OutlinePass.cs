using System.Collections.Generic;
using Supermarket.Components;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Supermarket.Rendering
{
    // Ported to the RenderGraph API: URP 17.6 (Unity 6000.6) no longer has the
    // Configure/Execute overrides this pass was originally written against.
    public class OutlinePass : ScriptableRenderPass
    {
        private readonly OutlineFeature.OutlineSettings _settings;
        private readonly Material _outlineMaterial;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private const string PassTag = "OutlinePass";

        private class PassData
        {
            public Material Material;
            public List<Renderer> Renderers;
        }

        public OutlinePass(OutlineFeature.OutlineSettings settings)
        {
            _settings = settings;

            // Create a material from our hidden shader
            var shader = Shader.Find("Hidden/UniversalOutline");
            if (shader != null)
            {
                _outlineMaterial = CoreUtils.CreateEngineMaterial(shader);
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_outlineMaterial == null) return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.isSceneViewCamera) return; // Don't run in scene view

            _renderers.Clear();
            foreach (var outlineController in OutlineController.AllOutlines)
            {
                if (outlineController.IsOutlineEnabled && outlineController.Renderer != null)
                {
                    _renderers.Add(outlineController.Renderer);
                }
            }
            if (_renderers.Count == 0) return;

            // Set material properties from settings
            _outlineMaterial.SetColor("_Color", _settings.OutlineColor);
            _outlineMaterial.SetFloat("_Thickness", _settings.OutlineThickness);

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(PassTag, out var passData))
            {
                passData.Material = _outlineMaterial;
                passData.Renderers = _renderers;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // Clear the stencil buffer so no data from earlier passes affects the outline.
                    context.cmd.ClearRenderTarget(RTClearFlags.Stencil, Color.clear, 1f, 0);

                    // 1. RENDER STENCIL: Draw selected objects to the stencil buffer using Pass 0
                    foreach (var renderer in data.Renderers)
                    {
                        context.cmd.DrawRenderer(renderer, data.Material, 0, 0);
                    }

                    // 2. RENDER OUTLINE: Draw the outlines using Pass 1, which has the stencil test
                    foreach (var renderer in data.Renderers)
                    {
                        context.cmd.DrawRenderer(renderer, data.Material, 0, 1);
                    }
                });
            }
        }
    }
}
