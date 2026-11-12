// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchPoint3DWindowTests
    {
        [UnityTest] public IEnumerator PointPreviewRendersPixelsInspectsRowsAndReleasesOwnedResources()
        {
            string root = Path.Combine(Path.GetTempPath(), "FP3DWindow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            FPWorkbenchPoint3DWindow window = null, restored = null; Texture2D readback = null;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance; Type type = typeof(FPWorkbenchPoint3DWindow);
            bool Busy(FPWorkbenchPoint3DWindow w) => (bool)type.GetProperty("Busy", flags).GetValue(w);
            try
            {
                string path = Path.Combine(root, "source.csv"); File.WriteAllText(path, "x,y,z\n1000000000000,0,0\n1000000000001,1,0\n1000000000002,0,1\n");
                var importing = FPWorkbenchCsvDataset.ImportAsync(path, Path.Combine(root, "cache"), new FPWorkbenchCsvProfile()); while (!importing.IsCompleted) yield return null;
                Assert.That(importing.IsFaulted, Is.False); var data = importing.Result;
                var shader = Shader.Find("Hidden/FuzzPhyte/Workbench/Points"); Assert.That(shader, Is.Not.Null); Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
                window = FPWorkbenchPoint3DWindow.Open("fixture", data, 0, 1, 2); window.position = new Rect(50, 50, 1000, 750);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((Busy(window) || type.GetField("previewTexture", flags).GetValue(window) == null) && DateTime.UtcNow < deadline) { window.Repaint(); yield return null; }
                Assert.That((string)type.GetField("error", flags).GetValue(window), Is.Null);
                var renderer = (FPWorkbenchPointRenderer)type.GetField("renderer", flags).GetValue(window); Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.PointCount, Is.EqualTo(3)); Assert.That(renderer.Mesh.vertexCount, Is.EqualTo(12)); Assert.That(renderer.Mesh.GetIndexCount(0), Is.EqualTo(18));
                var texture = type.GetField("previewTexture", flags).GetValue(window) as RenderTexture; Assert.That(texture, Is.Not.Null);
                var previous = RenderTexture.active;
                try { RenderTexture.active = texture; readback = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false); readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); readback.Apply(); }
                finally { RenderTexture.active = previous; }
                int colored = 0; foreach (var pixel in readback.GetPixels32()) if (pixel.b > pixel.r + 30 && pixel.g > pixel.r + 30) colored++;
                Assert.That(colored, Is.GreaterThan(5), "Preview must contain actual billboard pixels, not just a valid mesh.");
                var preview = (PreviewRenderUtility)type.GetField("preview", flags).GetValue(window); Rect rect = (Rect)type.GetField("previewRect", flags).GetValue(window);
                var projected = preview.camera.WorldToViewportPoint(renderer.Prepared.Positions[0]);
                type.GetMethod("Inspect", flags).Invoke(window, new object[] { rect, new Vector2(rect.x + projected.x * rect.width, rect.y + (1 - projected.y) * rect.height) });
                Assert.That((bool)type.GetField("selected", flags).GetValue(window), Is.True);
                Assert.That(((FPWorkbenchSpatialPoint)type.GetField("selection", flags).GetValue(window)).Row, Is.Zero);
                Assert.Throws<ArgumentOutOfRangeException>(() => renderer.SetAppearance(Color.cyan, 0));
                type.GetField("zoom", flags).SetValue(window, 2f); type.GetField("pan", flags).SetValue(window, new Vector3(.25f, 0, 0));
                var descriptor = (FPWorkbenchFigureDocument)type.GetMethod("DescribeFigure", flags).Invoke(window, null);
                Assert.That(descriptor.Kind, Is.EqualTo(FPWorkbenchFigureKind.Point3D)); Assert.That(descriptor.Spatial.ZColumn, Is.EqualTo(2));
                Assert.That(descriptor.Spatial.Zoom, Is.EqualTo(2)); descriptor.Validate();
                string json = EditorJsonUtility.ToJson(window); Mesh ownedMesh = renderer.Mesh; Material ownedMaterial = renderer.Material;
                window.Close(); UnityEngine.Object.DestroyImmediate(window); window = null;
                Assert.That(ownedMesh == null, Is.True); Assert.That(ownedMaterial == null, Is.True);
                restored = ScriptableObject.CreateInstance<FPWorkbenchPoint3DWindow>(); type.GetMethod("OnDisable", flags).Invoke(restored, null);
                EditorJsonUtility.FromJsonOverwrite(json, restored); type.GetMethod("OnEnable", flags).Invoke(restored, null); restored.Show();
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((Busy(restored) || type.GetField("renderer", flags).GetValue(restored) == null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(((FPWorkbenchPointRenderer)type.GetField("renderer", flags).GetValue(restored)).Prepared.SourceRows.Count, Is.EqualTo(3));
                Assert.That((string)type.GetField("error", flags).GetValue(restored), Is.Null);
                restored.Close(); UnityEngine.Object.DestroyImmediate(restored); restored = null;
                restored = (FPWorkbenchPoint3DWindow)type.GetMethod("OpenSaved", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "fixture", data, descriptor });
                deadline = DateTime.UtcNow.AddSeconds(10);
                while ((Busy(restored) || type.GetField("renderer", flags).GetValue(restored) == null) && DateTime.UtcNow < deadline) yield return null;
                Assert.That(((FPWorkbenchPointRenderer)type.GetField("renderer", flags).GetValue(restored)).PointCount, Is.EqualTo(3));
                Assert.That((float)type.GetField("zoom", flags).GetValue(restored), Is.EqualTo(2)); Assert.That(((Vector3)type.GetField("pan", flags).GetValue(restored)).x, Is.EqualTo(.25f));
            }
            finally
            {
                if (window != null) { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
                if (restored != null) { restored.Close(); UnityEngine.Object.DestroyImmediate(restored); }
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                Directory.Delete(root, true);
            }
        }
    }
}
