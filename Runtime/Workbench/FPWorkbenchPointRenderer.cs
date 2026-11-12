// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>Main-thread Unity mesh adapter. One bounded billboard mesh, no scene objects or per-point entities.</summary>
    public sealed class FPWorkbenchPointRenderer : IDisposable
    {
        public Mesh Mesh { get; private set; }
        public Material Material { get; private set; }
        public int PointCount { get; private set; }
        public FPWorkbenchPreparedPoints Prepared { get; private set; }
        public FPWorkbenchPointRenderer(Shader shader)
        {
            if (shader == null || !shader.isSupported) throw new ArgumentException("A supported point billboard shader is required.");
            Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        public void SetAppearance(Color color, float pixels)
        {
            if (Material == null) throw new ObjectDisposedException(nameof(FPWorkbenchPointRenderer));
            if (float.IsNaN(pixels) || float.IsInfinity(pixels) || pixels < 1 || pixels > 32) throw new ArgumentOutOfRangeException(nameof(pixels));
            for (int c = 0; c < 4; c++) if (float.IsNaN(color[c]) || float.IsInfinity(color[c]) || color[c] < 0 || color[c] > 1) throw new ArgumentException("Point color must be finite and normalized.");
            Material.SetColor("_Color", color); Material.SetFloat("_PointSize", pixels);
        }
        public void SetPoints(FPWorkbenchPreparedPoints points)
        {
            if (Material == null) throw new ObjectDisposedException(nameof(FPWorkbenchPointRenderer));
            if (points == null || points.Positions == null || points.SourceRows == null || points.Positions.Count > 65536 || points.Positions.Count != points.SourceRows.Count) throw new ArgumentException("Invalid prepared point batch.");
            int count = points.Positions.Count;
            if (count > 16383 && !SystemInfo.supports32bitsIndexBuffer) throw new NotSupportedException("This device requires a point budget of at most 16,383 for billboard indices.");
            var vertices = new Vector3[count * 4]; var uv = new Vector2[vertices.Length]; var indices = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int v = i * 4, t = i * 6; var p = points.Positions[i];
                vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = p;
                uv[v] = Vector2.zero; uv[v + 1] = Vector2.right; uv[v + 2] = Vector2.one; uv[v + 3] = Vector2.up;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2; indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            var next = new Mesh { name = "Workbench point billboards", hideFlags = HideFlags.HideAndDontSave, indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try { next.vertices = vertices; next.uv = uv; next.SetIndices(indices, MeshTopology.Triangles, 0); next.RecalculateBounds(); var bounds = next.bounds; bounds.Expand(.05f); next.bounds = bounds; }
            catch { Destroy(next); throw; }
            Destroy(Mesh); Mesh = next; PointCount = count; Prepared = points;
        }
        public void Dispose() { Destroy(Mesh); Destroy(Material); Mesh = null; Material = null; Prepared = null; PointCount = 0; }
        private static void Destroy(UnityEngine.Object value)
        { if (value == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
