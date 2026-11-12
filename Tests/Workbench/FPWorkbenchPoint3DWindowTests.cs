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
        [Test] public void AxisSegmentsClipAtViewportAndNearPlaneWithoutDroppingVisibleSections()
        {
            var go = new GameObject("Workbench segment camera");
            try {
                var camera = go.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 1; camera.aspect = 1;
                camera.nearClipPlane = .001f; camera.farClipPlane = 100;
                var method = typeof(FPWorkbenchPoint3DWindow).GetMethod("ProjectSegment", BindingFlags.Static | BindingFlags.NonPublic);
                object[] args = { camera, new Rect(0, 0, 100, 100), new Vector3(-2, 0, 1), new Vector3(2, 0, 1), null, null, true };
                Assert.That((bool)method.Invoke(null, args), Is.True, "Both endpoints may be offscreen while their middle section remains visible.");
                Assert.That(((Vector3)args[4]).x, Is.EqualTo(0).Within(.001)); Assert.That(((Vector3)args[5]).x, Is.EqualTo(100).Within(.001));
                args[2] = new Vector3(0, 0, -.1f); args[3] = new Vector3(0, .5f, .1f);
                Assert.That((bool)method.Invoke(null, args), Is.True, "Crossing the near plane should shorten a line, not hide it.");
                args[2] = new Vector3(-3, 0, 1); args[3] = new Vector3(-2, 0, 1);
                Assert.That((bool)method.Invoke(null, args), Is.False);
                camera.orthographic = false;
                args[2] = new Vector3(0, 0, .0005f); args[3] = new Vector3(0, 0, .1f);
                Assert.That((bool)method.Invoke(null, args), Is.True);
            } finally { UnityEngine.Object.DestroyImmediate(go); }
        }
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
                Assert.That(preview.camera.nearClipPlane, Is.EqualTo(.001f).Within(1e-7));
                var projected = preview.camera.WorldToViewportPoint(renderer.Prepared.Positions[0]);
                type.GetMethod("Inspect", flags).Invoke(window, new object[] { rect, new Vector2(rect.x + projected.x * rect.width, rect.y + (1 - projected.y) * rect.height) });
                Assert.That((bool)type.GetField("selected", flags).GetValue(window), Is.True);
                Assert.That(((FPWorkbenchSpatialPoint)type.GetField("selection", flags).GetValue(window)).Row, Is.Zero);
                var marker = new object[] { preview.camera, rect, null };
                Assert.That((bool)type.GetMethod("ProjectSelection", flags).Invoke(window, marker), Is.True);
                Assert.That(Vector2.Distance((Vector3)marker[2], new Vector2(rect.x + projected.x * rect.width, rect.y + (1 - projected.y) * rect.height)), Is.LessThan(.01f));
                var zero = new object[] { preview.camera, rect, null };
                Assert.That((bool)type.GetMethod("ProjectOrigin", flags).Invoke(window, zero), Is.False, "A local presentation center is not source-coordinate zero for large-origin datasets.");
                string previousTick = null;
                for (int tick = 0; tick <= 4; tick++) {
                    var tickArgs = new object[] { 0, tick, null };
                    var localTick = (Vector3)type.GetMethod("AxisTick", flags).Invoke(window, tickArgs);
                    var text = (string)tickArgs[2];
                    Assert.That(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1e12 + tick * .5), "Labels must use source doubles rather than normalized local coordinates.");
                    Assert.That(text, Is.Not.EqualTo(previousTick)); previousTick = text;
                    Assert.That(localTick.x, Is.EqualTo(-1 + tick * .5f).Within(1e-6));
                }
                type.GetField("showAxisValues", flags).SetValue(window, false);
                type.GetField("showXAxis", flags).SetValue(window, false); type.GetField("showYAxis", flags).SetValue(window, true);
                type.GetField("showZAxis", flags).SetValue(window, false); type.GetField("showOrigin", flags).SetValue(window, true);
                window.Repaint(); yield return null;
                Assert.That(type.GetField("renderer", flags).GetValue(window), Is.SameAs(renderer)); Assert.That(Busy(window), Is.False);
                Assert.Throws<ArgumentOutOfRangeException>(() => renderer.SetAppearance(Color.cyan, 0));
                type.GetField("zoom", flags).SetValue(window, 2f); type.GetField("pan", flags).SetValue(window, new Vector3(.25f, 0, 0));
                Undo.IncrementCurrentGroup();
                type.GetMethod("ApplyAppearancePreset", flags).Invoke(window, new object[] { new FPWorkbenchPoint3DPreset { Color = Color.magenta, PointSize = 9 }.ToJson() });
                Assert.That(type.GetField("renderer", flags).GetValue(window), Is.SameAs(renderer)); Assert.That(Busy(window), Is.False);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(type.GetField("pointSize", flags).GetValue(window), Is.EqualTo(4f));
                Undo.PerformRedo(); Assert.That(type.GetField("pointSize", flags).GetValue(window), Is.EqualTo(9f));
                Assert.That(type.GetField("pointColor", flags).GetValue(window), Is.EqualTo(Color.magenta));
                Assert.That(type.GetField("zoom", flags).GetValue(window), Is.EqualTo(2f));
                Assert.That(((Vector3)type.GetField("pan", flags).GetValue(window)).x, Is.EqualTo(.25f));
                Assert.That(type.GetField("xColumn", flags).GetValue(window), Is.EqualTo(0)); Assert.That(type.GetField("pointBudget", flags).GetValue(window), Is.EqualTo(10000));
                var descriptor = (FPWorkbenchFigureDocument)type.GetMethod("DescribeFigure", flags).Invoke(window, null);
                Assert.That(descriptor.Kind, Is.EqualTo(FPWorkbenchFigureKind.Point3D)); Assert.That(descriptor.Spatial.ZColumn, Is.EqualTo(2));
                Assert.That(descriptor.Spatial.Zoom, Is.EqualTo(2)); descriptor.Validate();
                Assert.That(descriptor.Spatial.PointSize, Is.EqualTo(9)); Assert.That(descriptor.Colors[0], Is.EqualTo(Color.magenta));
                Assert.That(descriptor.Spatial.ShowAxisValues, Is.False);
                Assert.That(descriptor.Spatial.NearClip, Is.EqualTo(.001f));
                Assert.That(descriptor.Spatial.ShowXAxis, Is.False); Assert.That(descriptor.Spatial.ShowYAxis, Is.True);
                Assert.That(descriptor.Spatial.ShowZAxis, Is.False); Assert.That(descriptor.Spatial.ShowOrigin, Is.True);
                type.GetField("showAxisValues", flags).SetValue(window, true);
                string imageOutput = Path.Combine(root, "points.fpfigure");
                var cameraPosition = preview.camera.transform.position; var activeTexture = RenderTexture.active;
                var pngWork0 = (System.Threading.Tasks.Task<FPWorkbenchImageExportResult>)type.GetMethod("ExportPng", flags).Invoke(window, new object[] { imageOutput, 640, 480 });
                Assert.That(preview.camera.transform.position, Is.EqualTo(cameraPosition)); Assert.That(RenderTexture.active, Is.SameAs(activeTexture));
                while (!pngWork0.IsCompleted) yield return null; pngWork0.GetAwaiter().GetResult();
                Assert.That(type.GetField("zoom", flags).GetValue(window), Is.EqualTo(2f)); Assert.That(Busy(window), Is.False);
                var exportedImage = new Texture2D(2, 2);
                try {
                    Assert.That(exportedImage.LoadImage(File.ReadAllBytes(Path.Combine(imageOutput, "figure.png"))), Is.True);
                    Assert.That(exportedImage.width, Is.EqualTo(640)); Assert.That(exportedImage.height, Is.EqualTo(480));
                    int magenta = 0; foreach (var pixel in exportedImage.GetPixels32()) if (pixel.r > pixel.g + 30 && pixel.b > pixel.g + 30) magenta++;
                    Assert.That(magenta, Is.GreaterThan(5), "Export must contain the rendered point appearance.");
                    int white = 0; foreach (var pixel in exportedImage.GetPixels32()) if (pixel.r > 180 && pixel.g > 180 && pixel.b > 180) white++;
                    Assert.That(white, Is.GreaterThan(100), "The exported image includes visible axis labels and point-count disclosure.");
                } finally { UnityEngine.Object.DestroyImmediate(exportedImage); }
                var provenance = JsonUtility.FromJson(File.ReadAllText(Path.Combine(imageOutput, "provenance.json")), type.GetNestedType("ImageProvenance", BindingFlags.NonPublic));
                Assert.That(provenance.GetType().GetField("Width").GetValue(provenance), Is.EqualTo(640));
                Assert.That(provenance.GetType().GetField("RenderedPoints").GetValue(provenance), Is.EqualTo(3));
                Assert.That(provenance.GetType().GetField("Annotated").GetValue(provenance), Is.True);
                Assert.That((string[])provenance.GetType().GetField("AxisLabels").GetValue(provenance), Is.EqualTo(new[] { "x", "y", "z" }));
                Assert.That(provenance.GetType().GetField("PointBudget").GetValue(provenance), Is.EqualTo(10000));
                Assert.That((double[])provenance.GetType().GetField("AxisBounds").GetValue(provenance), Is.EqualTo(new[] { 1e12, 1e12 + 2, 0d, 1d, 0d, 1d }));
                Assert.That(provenance.GetType().GetField("SourcePath").GetValue(provenance), Is.EqualTo(data.SourcePath));
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    Assert.That(provenance.GetType().GetField("PngSha256Base64").GetValue(provenance), Is.EqualTo(Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(Path.Combine(imageOutput, "figure.png"))))));
                Assert.That(Assert.Throws<TargetInvocationException>(() => type.GetMethod("ExportPng", flags).Invoke(window, new object[] { imageOutput, 640, 480 })).InnerException, Is.TypeOf<IOException>());
                string invalidOutput = Path.Combine(root, "invalid.fpfigure");
                Assert.That(Assert.Throws<TargetInvocationException>(() => type.GetMethod("ExportPng", flags).Invoke(window, new object[] { invalidOutput, 4096, 4096 })).InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(Directory.Exists(invalidOutput), Is.False);
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
                Assert.That((bool)type.GetField("showAxisValues", flags).GetValue(restored), Is.False);
                Assert.That((float)type.GetField("nearClip", flags).GetValue(restored), Is.EqualTo(.001f));
                Assert.That((bool)type.GetField("showXAxis", flags).GetValue(restored), Is.False);
                Assert.That((bool)type.GetField("showYAxis", flags).GetValue(restored), Is.True);
                Assert.That((bool)type.GetField("showZAxis", flags).GetValue(restored), Is.False);
                Assert.That((bool)type.GetField("showOrigin", flags).GetValue(restored), Is.True);
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
