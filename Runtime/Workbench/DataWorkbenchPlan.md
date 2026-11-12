# Data Workbench: delivery checklist

Agreed scope — 2026-11-12. **Completed 2026-11-12**, without extending the delivery scope. Evidence, timings, artifact locations and limitations: [WorkbenchDeliveryResults.md](../../Documentation~/WorkbenchDeliveryResults.md). The starting-point notes below describe the pre-delivery state.

## Deliverable and boundaries

Finish the reusable FP Analytics tabular viewer/plotter: workbooks with tables, light nondestructive corrections/calculated columns, 2D/3D plots and image exports. Use the whale recordings as demonstration data, without coupling Analytics to Biologging.

Keep runtime engines in `Runtime/Workbench`, Editor UI in `Editor/WorkbenchEditor`, and tests in `Tests/Workbench`. UnityEngine is allowed; runtime must have no UnityEditor dependency. Reuse existing services and avoid new packages. Review the current dirty worktree; preserve existing changes.

Defer Biologging integration, scientific/MATLAB equivalence, iPad qualification, Excel-style features, advanced global filtering/sorting, new alignment modes, independent-source overlays, and Entities/Burst redesign. These do not gate this delivery. Fix defects encountered in the agreed workflow; do not expand the roadmap.

## Starting point

- Implemented: background CSV indexing/pages, early unverified preview, workbook/sheet persistence, table navigation/layout, nondestructive corrections, limited formulas, Undo, plots and exports.
- Plots: scalar line/points, histograms, same-source overlays/dual axes, aligned scatter, and bounded 3D billboard-mesh points. 3D currently uses neither Entities nor Burst; retain it if the demonstration works.
- Disk quotas: `FPWorkbenchDiskCacheStore` is implemented/tested, but ordinary Workbench imports still bypass it. Editor settings, complete ownership of subsequently generated caches, active-view pins and regeneration after eviction remain unfinished. This is integration plus lifecycle/recovery work, not just a settings toggle.
- Last verified checkpoint: clean Unity compilation, 158/158 Workbench tests. This is prior evidence, not a fresh-context acceptance result or a RAM/latency benchmark.

## 1. Finish disk-cache integration

- [x] Add one small cache-settings area: dedicated root outside Assets, configurable quota (4 GiB is a starting default), used/reserved space, and clear pressure/error feedback. Persist settings as simple local JSON; runtime accepts explicit configuration without Editor hooks.
- [x] Route new imports and generated index/plot/alignment artifacts through owned-cache admission. Extend ownership handling for artifacts added after import; unexpected files must remain protected. Avoid implicit adoption/deletion of legacy caches.
- [x] Hold cache leases for tables, detached/shared plots, source verification and background workers. Release only when the final owner/worker finishes, including cancellation, close and script reload. Never evict an in-use entry.
- [x] Evict eligible unused caches in LRU order when space is required. Distinguish estimates/reservations from measured cache bytes. If pinned/protected content fills the quota, explain the refusal and recovery options; do not silently lose data or queue unlimited work.
- [x] Reopen a workbook after its unused cache is evicted: reconstruct from the original file, verify the exact source/import identity, then restore tables, corrections, calculated columns, figures and layout. Regenerate derived/aligned products from saved recipes. Update in-memory cache references only after success; saving the workbook remains explicit.
- [x] Reject changed/missing originals without retargeting annotations or replacing the current valid workbook. Preserve existing legacy workbook/cache compatibility. Originals, workbooks, correction files and user exports are never eviction targets.
- [x] Test quota pressure, shared-window pins, LRU, cancel/close/reload, regeneration, changed-source refusal, protected files, and failed publication. Inspect the actual reopened workbook in the Editor.

## 2. Walk through the real sample

Use this recovered CSV read-only:

`C:\GItHub\FPLibraries\BioLoggingData\RAW mn250117-P46_recovered\mn250117-P46\raw\csvs\mn250117-P46_001.csv`

Known successful intake: Windows-1252, comma-separated, 487,006 rows and 34 columns. Reconfirm it in the current build. Key columns (zero-based): UTC date 0, UTC time 1, acceleration X/Y/Z 4/5/6, depth 18, depth temperature 19. UTC format: `dd.MM.yyyy HH:mm:ss.ffffff`. Preserve declared units and ordering; apply no hidden calibration, filtering or resampling.

- [x] Open the sample through normal UI, observe preview-to-indexed-table transition, navigate distant rows, resize/hide columns and inspect values.
- [x] Save/reopen a workbook with multiple sheet views. Demonstrate a small correction, Undo/Redo and a supported calculated column in a demonstration copy; restore original measurements before the graph gallery.
- [x] Demonstrate plot creation from columns, zoom/pan, axes/grids, detached windows and 3D camera/point inspection. Fix workflow blockers rather than adding unrelated features.
- [x] Record basic real-file timings and process-memory observations for import, cached navigation, plot generation and reopen/regeneration. Record hardware/cache state and distinguish accounted buffers from total process RAM. Address visible hangs or unbounded growth; do not launch a general benchmarking project.

## 3. Produce a polished graph gallery

Use current graph/export features; select useful ranges from the actual data, with legible labels, units, grids and consistent styling. Export at 1920x1080 or another supported size within the existing 4,194,304-pixel limit. Keep full data distinct from displayed summaries/sampled points.

- [x] Full-recording time/depth overview: UTC or clearly labeled elapsed time; depth in metres, increasing downward.
- [x] An informative zoomed time/depth section, with its chosen range recorded.
- [x] Acceleration X/Y/Z over time in a same-source multiseries plot, units m/s².
- [x] A depth histogram with documented range, bin settings and invalid-value handling.
- [x] A 3D acceleration X/Y/Z point cloud with a useful camera angle, labeled axes/units and disclosed displayed point count/budget. This is a sensor-space cloud, not a reconstructed animal trajectory. If an existing 3D/export defect blocks it, fix that bounded defect.

Write results into a new run folder under `C:\GItHub\FPLibraries\BioLoggingData\Workbench_Delivery\`, outside Assets and the Analytics package. Deliver a saved `.fpworkbook`, five `.fpfigure` export folders (each `figure.png` plus `provenance.json`), and `Gallery.md` linking the images and recording source identity, bindings/ranges, settings and any limitations. Inspect the exported images themselves. Never overwrite raw data or existing exports; do not commit private recordings or generated gallery data to the package.

## 4. Close this delivery

- [x] Recompile and run appropriate connected-Unity tests; report actual results and any unresolved failures. No new packages or hand-authored Unity-generated files.
- [x] Verify the normal UI workflow and exported images, including a workbook reopen after deliberate eligible-cache eviction in an isolated managed store.
- [x] Leave a short usage README, this checklist with evidence/checkmarks, and a concise changelog. No more chronological implementation sections; detailed evidence belongs in a separate results note.
- [x] Report what users can now do, where the workbook/gallery are, and remaining limitations. This delivery is complete when cache integration/recovery and the demonstrated viewer/plotter/export workflow pass; deferred research/platform features remain deferred.

## Handoff references

Package root: `C:\GItHub\FPLibraries\FPLibraries\Assets\FP_Utility_Analytics`.

Read `Runtime/Workbench/README.md`, the source/asmdefs and applicable repository/skill instructions. Preserve the established runtime/Editor boundary. Historical detail is archived under `Documentation~/Archive/`; consult it only for specific API or validation context, not as an additional backlog. The user has no MATLAB reference export or agreed comparison tolerances; do not request them as a prerequisite for this delivery.
