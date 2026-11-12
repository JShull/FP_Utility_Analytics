// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.IO;
    using System.Text;
    using UnityEditor;
    internal static class FPWorkbenchPlotPresetGUI
    {
        public static void Draw(FPWorkbenchPlotSettings settings, bool yOnly = false)
            => DrawFileActions(() => FPWorkbenchPlotPreset.Capture(settings).ToJson(), json => {
                var preset = FPWorkbenchPlotPreset.Parse(json);
                if (yOnly) preset.ApplyY(settings); else preset.Apply(settings);
            }, yOnly ? "right Y preset" : "presentation preset", "fpplot", FPWorkbenchPlotPreset.MaximumBytes);

        internal static void DrawFileActions(Func<string> capture, Action<string> apply, string label, string extension, int maximumBytes)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (UnityEngine.GUILayout.Button("Load " + label + "…")) Execute(() => {
                    string path = EditorUtility.OpenFilePanel("Load " + label, "", extension); if (string.IsNullOrEmpty(path)) return;
                    using var input = File.OpenRead(path);
                    if (input.Length > maximumBytes) throw new InvalidDataException("Presentation preset exceeds its byte limit.");
                    var bytes = new byte[(int)input.Length]; int offset = 0;
                    while (offset < bytes.Length) { int n = input.Read(bytes, offset, bytes.Length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
                    apply(new UTF8Encoding(false, true).GetString(bytes));
                });
                if (UnityEngine.GUILayout.Button("Save " + label + "…")) Execute(() => {
                    string json = capture();
                    if (Encoding.UTF8.GetByteCount(json) > maximumBytes) throw new InvalidDataException("Presentation preset exceeds its byte limit.");
                    string path = EditorUtility.SaveFilePanel("Save " + label, "", "PlotPresentation", extension); if (string.IsNullOrEmpty(path)) return;
                    if (!path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)) throw new IOException("Use a ." + extension + " filename.");
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllText(temporary, json, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                });
            }
        }
        private static void Execute(Action action) { try { action(); } catch (Exception ex) { EditorUtility.DisplayDialog("Plot preset", ex.Message, "OK"); } }
    }
}
