// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.IO;
    using System.Text;
    using UnityEditor;
    internal static class FPWorkbenchPlotPresetGUI
    {
        public static void Draw(FPWorkbenchPlotSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (UnityEngine.GUILayout.Button("Load presentation preset…")) Execute(() => {
                    string path = EditorUtility.OpenFilePanel("Load plot presentation", "", "fpplot"); if (string.IsNullOrEmpty(path)) return;
                    using var input = File.OpenRead(path);
                    if (input.Length > FPWorkbenchPlotPreset.MaximumBytes) throw new InvalidDataException("Plot preset exceeds 16 KiB.");
                    var bytes = new byte[(int)input.Length]; int offset = 0;
                    while (offset < bytes.Length) { int n = input.Read(bytes, offset, bytes.Length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
                    FPWorkbenchPlotPreset.Parse(new UTF8Encoding(false, true).GetString(bytes)).Apply(settings);
                });
                if (UnityEngine.GUILayout.Button("Save presentation preset…")) Execute(() => {
                    string json = FPWorkbenchPlotPreset.Capture(settings).ToJson();
                    string path = EditorUtility.SaveFilePanel("Save plot presentation", "", "PlotPresentation", "fpplot"); if (string.IsNullOrEmpty(path)) return;
                    if (!path.EndsWith(".fpplot", StringComparison.OrdinalIgnoreCase)) throw new IOException("Use a .fpplot filename.");
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllText(temporary, json, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                });
            }
        }
        private static void Execute(Action action) { try { action(); } catch (Exception ex) { EditorUtility.DisplayDialog("Plot preset", ex.Message, "OK"); } }
    }
}
