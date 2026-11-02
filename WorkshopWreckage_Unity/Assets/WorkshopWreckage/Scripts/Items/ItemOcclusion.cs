using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage
{
    /// <summary>Line of sight for the item batches (performance pass 2026-11-01): while the player's camera renders, a
    /// pass injected into its renderer (HiZPass, after the opaques) reduces the camera's depth texture on the GPU to a coarse grid (WWHiZ.compute, farthest eye depth per cell) and read
    /// back asynchronously. A box is hidden when its nearest point lies beyond the farthest depth in every cell it
    /// covers in that frame, so shelves, machines, walls and heaps hide what is behind them, and gaps between shelves
    /// don't. The grid is a frame or two old: the box grows by how far the camera moved since, and a box reaching
    /// outside that frame's view, or crossing its near plane, counts as seen. No data: nothing is hidden.</summary>
    public class ItemOcclusion
    {
        public const int GridW = 160, GridH = 90;

        readonly ComputeShader _cs;
        readonly int _kernel;
        GraphicsBuffer _out;
        readonly HiZPass _pass;
        bool _pending, _dispatched;
        // the newest grid and the camera it was rendered with
        float[] _grid, _incoming;
        Matrix4x4 _view, _proj, _inView, _inProj;
        Vector3 _eye, _inEye;
        float _near, _inNear;
        bool _ready;
        public bool FlipY;   // grid row 0 is the top of the screen (decided from the first grids, see Calibrate)
        int _calibrated;
        Camera _cam;

        public static ItemOcclusion Create()
        {
            var cs = Resources.Load<ComputeShader>("WWHiZ");
            if (cs == null || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback) return null;
            return new ItemOcclusion(cs);
        }

        ItemOcclusion(ComputeShader cs)
        {
            _cs = cs;
            _kernel = cs.FindKernel("Reduce");
            _out = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GridW * GridH, 4);
            _grid = new float[GridW * GridH];
            _incoming = new float[GridW * GridH];
            _pass = new HiZPass(this) { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            _out?.Release();
            _out = null;
        }

        /// <summary>The camera whose depth is used (the player's); null stops the reductions and drops the grid.</summary>
        public void Track(Camera cam)
        {
            if (cam != _cam) _ready = false;
            _cam = cam;
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_out == null || cam != _cam || _pending) return;
            var renderer = cam.GetUniversalAdditionalCameraData()?.scriptableRenderer;
            if (renderer == null) return;
            _inView = cam.worldToCameraMatrix;
            _inProj = cam.projectionMatrix;
            _inEye = cam.transform.position;
            _inNear = cam.nearClipPlane;
            float n = cam.nearClipPlane, f = cam.farClipPlane;
            // Unity's _ZBufferParams z, w: eye depth = 1 / (z * raw + w)
            float x = SystemInfo.usesReversedZBuffer ? -1 + f / n : 1 - f / n;
            float w = SystemInfo.usesReversedZBuffer ? 1f / f : (f / n) / f;
            _pass.zb = new Vector4(x / f, w, 0, 0);
            _dispatched = false;
            renderer.EnqueuePass(_pass);
        }

        /// <summary>Injected into the player camera's renderer: the reduction, on the camera's depth texture.</summary>
        class HiZPass : ScriptableRenderPass
        {
            readonly ItemOcclusion _o;
            public Vector4 zb;
            class Data
            {
                public ComputeShader cs;
                public int kernel, w, h;
                public Vector4 zb;
                public TextureHandle depth;
                public BufferHandle output;
                public ItemOcclusion owner;
            }
            public HiZPass(ItemOcclusion o) { _o = o; profilingSampler = new ProfilingSampler("WW item Hi-Z"); }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                var depth = res.cameraDepthTexture;
                if (!depth.IsValid() || _o._out == null) return;
                var desc = renderGraph.GetTextureDesc(depth);
                using var builder = renderGraph.AddComputePass<Data>("WW item Hi-Z", out var data, profilingSampler);
                data.cs = _o._cs; data.kernel = _o._kernel; data.zb = zb; data.owner = _o;
                data.w = desc.width; data.h = desc.height;
                data.depth = depth;
                data.output = renderGraph.ImportBuffer(_o._out);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseBuffer(data.output, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data d, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeTextureParam(d.cs, d.kernel, "_Depth", d.depth);
                    ctx.cmd.SetComputeBufferParam(d.cs, d.kernel, "_Out", d.output);
                    ctx.cmd.SetComputeIntParams(d.cs, "_Dims", d.w, d.h, GridW, GridH);
                    ctx.cmd.SetComputeVectorParam(d.cs, "_ZB", d.zb);
                    ctx.cmd.DispatchCompute(d.cs, d.kernel, (GridW + 7) / 8, (GridH + 7) / 8, 1);
                    d.owner._dispatched = true;
                });
            }
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_out == null || cam != _cam || _pending || !_dispatched) return;
            _dispatched = false;
            _pending = true;
            AsyncGPUReadback.Request(_out, r =>
            {
                _pending = false;
                if (r.hasError || _out == null) return;
                r.GetData<float>().CopyTo(_incoming);
                (_grid, _incoming) = (_incoming, _grid);
                _view = _inView; _proj = _inProj; _eye = _inEye; _near = _inNear;
                if (_calibrated < 8) Calibrate();
                _ready = true;
            });
        }

        /// <summary>Which way up the grid is: the camera looks along the floor or down at it, so the bottom of the
        /// screen is nearer than the top. Decided from the first grids where the halves clearly differ.</summary>
        void Calibrate()
        {
            double top = 0, bottom = 0;
            for (int y = 0; y < GridH / 4; y++)
                for (int x = 0; x < GridW; x++)
                {
                    bottom += _grid[y * GridW + x];
                    top += _grid[(GridH - 1 - y) * GridW + x];
                }
            if (System.Math.Abs(top - bottom) < 0.2 * System.Math.Max(top, bottom)) return;
            bool flip = top < bottom;   // row 0 nearer than the last row: row 0 is the bottom unless flipped
            if (_calibrated == 0) FlipY = flip;
            else if (flip != FlipY) { FlipY = flip; _calibrated = 0; }
            _calibrated++;
            if (_calibrated == 8) Debug.Log($"[occlusion] depth grid {GridW}x{GridH}, row 0 = {(FlipY ? "top" : "bottom")} of the screen");
        }

        /// <summary>True when the box is hidden behind what the camera saw (see the class summary).</summary>
        public bool Hidden(Bounds b, Vector3 eyeNow)
        {
            if (!_ready || _calibrated < 8) return false;
            b.Expand(2f * Vector3.Distance(eyeNow, _eye) + 0.1f);
            var vp = _proj * _view;
            float minX = 1, minY = 1, maxX = 0, maxY = 0, nearest = float.MaxValue;
            var c = b.center; var e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                float eye = -_view.MultiplyPoint3x4(p).z;
                if (eye < _near + 0.05f) return false;   // reaches the camera
                nearest = Mathf.Min(nearest, eye);
                var clip = vp * new Vector4(p.x, p.y, p.z, 1);
                float u = clip.x / clip.w * 0.5f + 0.5f, v = clip.y / clip.w * 0.5f + 0.5f;
                minX = Mathf.Min(minX, u); maxX = Mathf.Max(maxX, u);
                minY = Mathf.Min(minY, v); maxY = Mathf.Max(maxY, v);
            }
            if (minX < 0 || minY < 0 || maxX > 1 || maxY > 1) return false;   // partly outside that frame's view
            int x0 = (int)(minX * GridW), x1 = Mathf.Min(GridW - 1, (int)(maxX * GridW));
            int y0 = (int)(minY * GridH), y1 = Mathf.Min(GridH - 1, (int)(maxY * GridH));
            for (int y = y0; y <= y1; y++)
            {
                int row = FlipY ? GridH - 1 - y : y;
                for (int x = x0; x <= x1; x++)
                    if (_grid[row * GridW + x] >= nearest) return false;
            }
            return true;
        }
    }
}
