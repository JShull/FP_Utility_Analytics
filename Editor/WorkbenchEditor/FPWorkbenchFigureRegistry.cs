// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench.Editor
{
    using System;
    using System.Collections.Generic;

    internal static class FPWorkbenchFigureRegistry
    {
        private static readonly Dictionary<string, WeakReference<FPWorkbenchFigureSession>> figures = new Dictionary<string, WeakReference<FPWorkbenchFigureSession>>();
        public static FPWorkbenchFigureSession.Lease TryRetain(string id)
        {
            if (figures.TryGetValue(id, out var reference))
            {
                if (reference.TryGetTarget(out var session) && !session.IsDisposed) return session.Retain();
                figures.Remove(id);
            }
            return null;
        }
        public static FPWorkbenchFigureSession.Lease Retain(string id, FPWorkbenchCsvDataset dataset, FPWorkbenchSeriesBinding binding, FPWorkbenchPlotSettings settings, FPWorkbenchChangeSnapshot changes = null, FPWorkbenchFormulaProgram calculations = null)
        {
            if (figures.Count > 32)
            {
                var expired = new List<string>();
                foreach (var pair in figures) if (!pair.Value.TryGetTarget(out var value) || value.IsDisposed) expired.Add(pair.Key);
                foreach (string key in expired) figures.Remove(key);
            }
            var existing = TryRetain(id);
            if (existing != null)
            {
                if (existing.Session.DatasetId == dataset.DatasetId) return existing;
                existing.Dispose(); throw new InvalidOperationException("Figure identity is already bound to a different source revision.");
            }
            var session = new FPWorkbenchFigureSession(dataset, binding, settings, changes, calculations);
            figures[id] = new WeakReference<FPWorkbenchFigureSession>(session); return session.Retain();
        }
    }
}
