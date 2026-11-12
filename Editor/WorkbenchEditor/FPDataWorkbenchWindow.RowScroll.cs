// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    public sealed partial class FPDataWorkbenchWindow
    {
        private Rect rowScrollTrack, rowScrollThumb;
        private int rowScrollControl, rowScrollCapture;
        private float rowScrollGrab;
        private Sheet rowScrollSheet;
        private const float RowScrollWidth = 16;
        internal static long ScrollRow(double fraction, long rowCount, int visibleRows)
        {
            if (rowCount < 0 || visibleRows < 1 || double.IsNaN(fraction) || double.IsInfinity(fraction)) throw new ArgumentOutOfRangeException();
            long maximum = Math.Max(0, rowCount - visibleRows);
            if (fraction <= 0) return 0;
            if (fraction >= 1) return maximum;
            double value = Math.Round(maximum * fraction);
            return value >= maximum ? maximum : Math.Max(0, (long)value);
        }
        private void DrawRowScroll(Sheet sheet, Rect rect)
        {
            int visibleRows = Math.Max(1, Math.Min(RowsPerPage, (int)(rect.height / 20)));
            long maximum = Math.Max(0, sheet.Dataset.RowCount - visibleRows);
            rowScrollControl = GUIUtility.GetControlID(764915, FocusType.Passive);
            var up = new Rect(rect.x, rect.y, rect.width, 16);
            var down = new Rect(rect.x, rect.yMax - 16, rect.width, 16);
            using (new EditorGUI.DisabledScope(sheet.FirstRow <= 0))
                if (GUI.Button(up, "▲", EditorStyles.miniButton)) Move(sheet, -1);
            using (new EditorGUI.DisabledScope(sheet.FirstRow >= maximum))
                if (GUI.Button(down, "▼", EditorStyles.miniButton)) Move(sheet, 1);
            Rect track = new Rect(rect.x, up.yMax, rect.width, Math.Max(1, down.y - up.yMax));
            float height = maximum == 0 ? track.height : Math.Min(track.height, Math.Max(24, (float)(visibleRows / (double)Math.Max(1, sheet.Dataset.RowCount)) * track.height));
            double fraction = maximum == 0 ? 0 : Math.Min(1, sheet.FirstRow / (double)maximum);
            Rect thumb = new Rect(track.x + 2, track.y + (float)fraction * (track.height - height), Math.Max(1, track.width - 4), height);
            if (Event.current.type == EventType.Repaint)
            {
                rowScrollTrack = track; rowScrollThumb = thumb;
                GUI.skin.verticalScrollbar.Draw(track, false, false, false, false);
                GUI.skin.verticalScrollbarThumb.Draw(thumb, thumb.Contains(Event.current.mousePosition), rowScrollSheet != null && GUIUtility.hotControl == rowScrollCapture, false, false);
            }
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && maximum > 0 && track.Contains(e.mousePosition))
            {
                rowScrollSheet = sheet; rowScrollCapture = rowScrollControl; GUIUtility.hotControl = rowScrollCapture;
                rowScrollGrab = thumb.Contains(e.mousePosition) ? e.mousePosition.y - thumb.y : height * .5f;
                sheet.FirstRow = ScrollRow((e.mousePosition.y - rowScrollGrab - track.y) / Math.Max(1, track.height - height), sheet.Dataset.RowCount, visibleRows);
                e.Use(); Repaint();
            }
            if (GUIUtility.hotControl == rowScrollCapture && rowScrollSheet != null)
            {
                if (e.type == EventType.MouseDrag)
                {
                    rowScrollSheet.FirstRow = ScrollRow((e.mousePosition.y - rowScrollGrab - track.y) / Math.Max(1, track.height - height), rowScrollSheet.Dataset.RowCount, visibleRows);
                    e.Use(); Repaint();
                }
                else if (e.type == EventType.MouseUp || (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape))
                { rowScrollSheet = null; GUIUtility.hotControl = 0; e.Use(); }
            }
        }
    }
}
