// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Tests
{
    using System;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using FuzzPhyte.Utility.Analytics.WorkBench.Editor;

    public sealed class FPWorkbenchPresentationUndoTests
    {
        [Test] public void AuthoringUndoPreservesNavigationAndAutomaticLimits()
        {
            Type type = typeof(FPDataWorkbenchWindow).Assembly.GetType("FuzzPhyte.Utility.Analytics.WorkBench.Editor.FPWorkbenchPresentationUndo", true);
            var helper = (IDisposable)Activator.CreateInstance(type, new object[] { (Action)(() => { }) });
            var settings = new FPWorkbenchPlotSettings { StartX = 10, EndX = 20, MinimumY = 2, MaximumY = 8 };
            void Begin() => type.GetMethod("Begin").Invoke(helper, new object[] { settings });
            void End() => type.GetMethod("End").Invoke(helper, null);
            try
            {
                Undo.IncrementCurrentGroup(); Begin();
                settings.Title = "Depth"; settings.XLabel = "Time (s)"; settings.YLabel = "Depth (m)";
                settings.XScale = FPWorkbenchAxisScale.Log10; settings.YScale = FPWorkbenchAxisScale.Log10;
                settings.Style = FPWorkbenchPlotStyle.Points; settings.MarkerSize = 8; settings.Grid = false; End();
                settings.StartX = 50; settings.EndX = 75; settings.MinimumY = -10; settings.MaximumY = 50;
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(settings.XScale, Is.EqualTo(FPWorkbenchAxisScale.Linear));
                Assert.That(settings.Title, Is.Empty); Assert.That(settings.Style, Is.EqualTo(FPWorkbenchPlotStyle.Line));
                Assert.That(settings.StartX, Is.EqualTo(50)); Assert.That(settings.EndX, Is.EqualTo(75));
                Assert.That(settings.MinimumY, Is.EqualTo(-10)); Assert.That(settings.MaximumY, Is.EqualTo(50));
                Undo.PerformRedo(); Assert.That(settings.XScale, Is.EqualTo(FPWorkbenchAxisScale.Log10)); Assert.That(settings.Title, Is.EqualTo("Depth")); Assert.That(settings.MarkerSize, Is.EqualTo(8));
                Assert.That(settings.YLabel, Is.EqualTo("Depth (m)")); Assert.That(settings.Grid, Is.False);
                var other = (IDisposable)Activator.CreateInstance(type, new object[] { (Action)(() => { }) });
                try
                {
                    Undo.IncrementCurrentGroup(); type.GetMethod("Begin").Invoke(other, new object[] { settings });
                    settings.Title = "Second view"; type.GetMethod("End").Invoke(other, null);
                    Begin(); End(); // A repaint of another shared view must not rewrite its Undo proxy.
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(settings.Title, Is.EqualTo("Depth"));
                    Undo.PerformUndo(); Assert.That(settings.Title, Is.Empty);
                    Undo.PerformRedo(); Assert.That(settings.Title, Is.EqualTo("Depth"));
                    Undo.PerformRedo(); Assert.That(settings.Title, Is.EqualTo("Second view"));
                    Assert.That(settings.StartX, Is.EqualTo(50)); Assert.That(settings.EndX, Is.EqualTo(75));
                }
                finally { other.Dispose(); }
                Undo.IncrementCurrentGroup(); Begin(); settings.LockY = true; settings.MinimumY = -2; settings.MaximumY = 2; End();
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(settings.LockY, Is.False);
                Undo.PerformRedo(); Assert.That(settings.LockY, Is.True); Assert.That(settings.MinimumY, Is.EqualTo(-2)); Assert.That(settings.MaximumY, Is.EqualTo(2));
                Assert.That(settings.StartX, Is.EqualTo(50)); Assert.That(settings.EndX, Is.EqualTo(75));
            }
            finally { helper.Dispose(); }
        }
    }
}
