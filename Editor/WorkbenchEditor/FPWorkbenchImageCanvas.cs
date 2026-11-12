// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>Bounded offscreen pixel-coordinate meshes/text, owned by one explicit image action.</summary>
    internal sealed class FPWorkbenchImageCanvas : IDisposable
    {
        private readonly PreviewRenderUtility preview = new PreviewRenderUtility();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> indices = new List<int>();
        private Mesh mesh;
        private Material material;
        private int labels;
        public void Quad(float x, float y, float width, float height, Color color)
        {
            if (vertices.Count > 1048572) throw new InvalidOperationException("Image geometry exceeds its vertex budget.");
            int v = vertices.Count;
            vertices.Add(new Vector3(x, y)); vertices.Add(new Vector3(x + width, y));
            vertices.Add(new Vector3(x + width, y + height)); vertices.Add(new Vector3(x, y + height));
            for (int i = 0; i < 4; i++) colors.Add(color);
            indices.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
        }
        public void Line(float x1, float y1, float x2, float y2, Color color, float width = 1)
        {
            if (vertices.Count > 1048572) throw new InvalidOperationException("Image geometry exceeds its vertex budget.");
            var delta = new Vector2(x2 - x1, y2 - y1); var normal = delta.sqrMagnitude > 0 ? new Vector2(-delta.y, delta.x).normalized * width * .5f : new Vector2(width * .5f, 0);
            int v = vertices.Count;
            vertices.Add(new Vector3(x1 + normal.x, y1 + normal.y)); vertices.Add(new Vector3(x2 + normal.x, y2 + normal.y));
            vertices.Add(new Vector3(x2 - normal.x, y2 - normal.y)); vertices.Add(new Vector3(x1 - normal.x, y1 - normal.y));
            for (int i = 0; i < 4; i++) colors.Add(color);
            indices.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
        }
        public void Text(string value, float x, float y, TextAnchor anchor = TextAnchor.MiddleCenter, float angle = 0)
        {
            if (++labels > 512 || value != null && value.Length > 2048) throw new InvalidOperationException("Image label budget exceeded.");
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Workbench image label") { hideFlags = HideFlags.HideAndDontSave };
            preview.AddSingleGO(go);
            var text = go.AddComponent<TextMesh>(); text.font = font; text.fontSize = 42; text.characterSize = 3.5f;
            text.anchor = anchor; text.alignment = TextAlignment.Center; text.color = Color.white; text.text = value;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material; go.transform.position = new Vector3(x, y, -1);
            go.transform.rotation = Quaternion.Euler(0, 0, angle);
        }
        public Texture2D Render(int width, int height)
        {
            var shader = Shader.Find("Hidden/Internal-Colored"); if (shader == null) throw new InvalidOperationException("Image shader is unavailable.");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off); material.SetInt("_ZWrite", 0); material.SetInt("_ZTest", (int)CompareFunction.Always);
            mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
            preview.BeginStaticPreview(new Rect(0, 0, width, height)); var camera = preview.camera;
            camera.transform.SetPositionAndRotation(new Vector3(width / 2f, height / 2f, -10), Quaternion.identity);
            camera.orthographic = true; camera.orthographicSize = height / 2f; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.Color; camera.backgroundColor = new Color(.1f, .1f, .1f);
            preview.DrawMesh(mesh, Matrix4x4.identity, material, 0); preview.Render(true); return preview.EndStaticPreview();
        }
        public void Dispose() { preview.Cleanup(); if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); if (material != null) UnityEngine.Object.DestroyImmediate(material); }
    }
}
