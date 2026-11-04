using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>URP RenderGraph: depth-tested architecture mask and one local composite.</summary>
public sealed class SelectiveSceneryFeature : ScriptableRendererFeature
{
    public Shader maskShader;
    public Shader compositeShader;
    Material[] masks;
    Material composite;
    SceneryPass pass;
    public override void Create()
    {
        Release();
        if (!maskShader || !compositeShader) return;
        masks = new Material[3];
        float[] weights = { 1f, .8f, .7f };
        for (int i = 0; i < 3; i++)
        {
            masks[i] = CoreUtils.CreateEngineMaterial(maskShader);
            masks[i].SetFloat("_Strength", weights[i]);
        }
        composite = CoreUtils.CreateEngineMaterial(compositeShader);
        pass = new SceneryPass(masks, composite) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (pass == null || renderingData.cameraData.renderType != CameraRenderType.Base || camera.cameraType != CameraType.Game) return;
        if (!camera.TryGetComponent<SelectiveSceneryAtmosphere>(out var settings) || !settings.isActiveAndEnabled || !settings.HasTargets) return;
        renderer.EnqueuePass(pass);
    }
    void Release()
    {
        if (masks != null) foreach (var m in masks) CoreUtils.Destroy(m);
        CoreUtils.Destroy(composite); masks = null; composite = null; pass = null;
    }
    protected override void Dispose(bool disposing) => Release();

    sealed class SceneryPass : ScriptableRenderPass
    {
        readonly Material[] masks;
        readonly Material composite;
        static readonly int MaskId = Shader.PropertyToID("_SelectiveSceneryMask");
        static readonly List<ShaderTagId> Tags = new List<ShaderTagId> {
            new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("UniversalForward"),
            new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("LightweightForward") };
        sealed class MaskData { public RendererListHandle stone, roof, timber; }
        sealed class CompositeData
        {
            public TextureHandle source, mask;
            public Material material;
            public Vector4 settings;
            public Color haze;
            public float debug;
        }
        public SceneryPass(Material[] masks, Material composite)
        {
            this.masks = masks; this.composite = composite;
            requiresIntermediateTexture = true;
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var resources = frame.Get<UniversalResourceData>();
            var camera = frame.Get<UniversalCameraData>();
            var rendering = frame.Get<UniversalRenderingData>();
            var lights = frame.Get<UniversalLightData>();
            var settings = camera.camera.GetComponent<SelectiveSceneryAtmosphere>();
            if (!settings || resources.isActiveTargetBackBuffer) return;
            var source = resources.activeColorTexture;
            var desc = graph.GetTextureDesc(source);
            desc.name = "Selective scenery visibility mask";
            desc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
            desc.clearBuffer = true; desc.clearColor = Color.black;
            desc.depthBufferBits = DepthBits.None;
            var mask = graph.CreateTexture(desc);
            using (var builder = graph.AddRasterRenderPass<MaskData>("Scenery · visible architecture only", out var data))
            {
                RendererListHandle Make(int kind)
                {
                    var drawing = RenderingUtils.CreateDrawingSettings(Tags, rendering, camera, lights, camera.defaultOpaqueSortFlags);
                    drawing.overrideMaterial = masks[kind]; drawing.overrideMaterialPassIndex = 0;
                    var filter = new FilteringSettings(RenderQueueRange.opaque, -1, SelectiveSceneryAtmosphere.Layer(kind));
                    return graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filter));
                }
                data.stone = Make(0); data.roof = Make(1); data.timber = Make(2);
                builder.UseRendererList(data.stone); builder.UseRendererList(data.roof); builder.UseRendererList(data.timber);
                builder.SetRenderAttachment(mask, 0);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc(static (MaskData d, RasterGraphContext context) => {
                    context.cmd.DrawRendererList(d.stone); context.cmd.DrawRendererList(d.roof); context.cmd.DrawRendererList(d.timber);
                });
            }
            var destinationDesc = graph.GetTextureDesc(source);
            destinationDesc.name = "Camera color · selective scenery"; destinationDesc.clearBuffer = false;
            var destination = graph.CreateTexture(destinationDesc);
            using (var builder = graph.AddRasterRenderPass<CompositeData>("Scenery · local surface softening", out var data))
            {
                data.source = source; data.mask = mask; data.material = composite;
                data.settings = new Vector4(settings.radiusAt1600 * camera.cameraTargetDescriptor.width / 1600f,
                    settings.softening, settings.desaturation, settings.haze);
                data.haze = settings.hazeColor; data.debug = settings.showMask ? 1 : 0;
                builder.UseTexture(source, AccessFlags.Read); builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0); builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (CompositeData d, RasterGraphContext context) => {
                    context.cmd.SetGlobalTexture(MaskId, d.mask);
                    d.material.SetVector("_Treatment", d.settings); d.material.SetColor("_Haze", d.haze);
                    d.material.SetFloat("_ShowMask", d.debug);
                    Blitter.BlitTexture(context.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }
            resources.cameraColor = destination;
        }
    }
}
