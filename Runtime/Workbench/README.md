# FP Analytics Data Workbench

A reusable Unity tabular viewer and plotter for large CSV recordings. Workbooks reference original data; corrections and calculated columns are separate metadata. Core engines live in `Runtime/Workbench`; Unity Editor UI lives in `Editor/WorkbenchEditor`.

## Open and use

1. Open **FuzzPhyte > Utility > Analytics > Data Workbench**.
2. Click **Workbook Settings…** at the top of the parameter sidebar. This dockable panel contains workbook open/save/recovery, saved figures/registered sources, shared cache settings, correction-file import/export and CSV import. Choose the source encoding, browse to a CSV, then **Add sheet**. A bounded read-only prefix can appear before full indexing; it is explicitly unverified. Editing and plots wait for source verification.
3. Navigate the streamed table using row/column controls and its vertical scrollbar. Resize column edges, reset widths to Auto, or organize hidden/frozen columns. Workbook metadata preserves layouts. Small `C1`, `C2`, etc. header labels identify formula source references; calculated definitions show `D1`, `D2`, etc. Labels follow source/definition order and remain correct when columns are hidden, frozen or reordered.
   Drag the divider above the embedded graph to adjust its height. Active sheet and Row Range can be collapsed independently, with window state preserved across reloads. In Active sheet, **Cell grid** shows table lines; when enabled, **Grid opacity** adjusts their opacity from 0 to 1 (default 0.35). These settings are independent per sheet and persist with Save workbook. Sheet parameter sections remain in the sidebar; graph settings live in the separate Plot Settings panel.
4. Open **Plot Settings…** near the top of the sidebar. Choose numeric columns and the X binding, then **Apply binding / build plot**. Column drag targets and dedicated figure windows support plot setup. Use explicit UTC formats for date/time pairs.
5. In Workbook Settings, use **Save workbook** / **Open workbook** for tables, source references, corrections, calculations, figures and layout. Original recordings are never rewritten.
6. In Workbook Settings, expand **Disk cache** to inspect usage, trim unused entries or change the dedicated root/quota. Close tables and figures and finish workers before applying a new root/quota. Settings are local JSON outside Assets. The settings panel can be closed or docked like a normal Unity Editor tab; closing it leaves its owning workbook and workers open. Each Workbench window has its own associated settings panel.

## Plot Settings and Figure Window

**Plot Settings…** opens one dockable, movable, closable Unity panel. It follows the focused Workbench sheet or scalar Figure Window, shows its target name, and stays on that target while you edit. Independent collapsible sections contain series binding, presentation presets/labels/axis scales, view range/inversion, line/step/point style, grid/Y limits, and framing. Binding changes use **Apply binding / build plot**; presentation changes appear immediately in both the embedded graph and its detached Figure Window. Existing Undo and workbook persistence remain in place. Closing the settings panel leaves the workbook and figures running; it owns no data-session lease.

The **Figure Window** keeps the graph prominent with a compact Fit X / Plot Settings / Export toolbar, mouse-wheel zoom and middle-drag pan. Click a represented sample for a yellow selection ring and row/Y readout. **Show row** navigates its open source sheet with the Y column in view and axis cells boxed; **Clear point** removes the selection. A changed view discards stale inspection state. Detached figures remain editable after the source sheet closes; Show row requires that sheet to be open. Export PNG opens the target figure's image-export controls in Plot Settings.

## Sheet Row Range

After the correction controls/resource readout, **Row Range** accepts **First row** and **Last row**, both inclusive and matching the table's 1-based row numbers. Click **Apply row range**, or **Use all rows** to reset. The table keeps all original rows inspectable, dims excluded rows and marks the boundaries. Its **Plot rows** reminder offers **F−/F+** and **L−/L+** to move either boundary one row, plus **Go first/Go last** navigation.

Scalar, detached scalar, histogram, same-source overlay, scatter and 3D figures opened from a sheet inherit its span. Applying a new span updates open figure panels in place, preserving docking and presentation/camera settings. Exclusion happens before statistics, extrema summaries and point sampling; source-row identities and raw data stay unchanged. UTC plots label elapsed time from the first included UTC timestamp. Range changes support plot-binding Undo/Redo.

Sheet spans and figure links/snapshots save with the workbook and image provenance. Existing unlinked figures for the same source adopt the sheet when its range is first applied. A figure whose source sheet has been closed retains its saved span; workbook-level aligned comparisons retain their explicit alignment-recipe scope. Materialized CSV export retains its own explicit export range.

## Point inspection and source units

Click a represented sample in the embedded plot to mark it and navigate the table to its original row, bringing the Y column into view. Only the associated axis cells are boxed; UTC X includes its date/time columns, while row-domain X uses the row-number cell. X cells are boxed when visible. **Clear point** removes this transient selection. Overview envelopes resolve to actual represented extrema/endpoints; zoom to inspect individual samples. Picking reads the cache in a bounded background job and respects row spans, axis scales and inversion.

**Source units** offers built-in presets and a **Custom override** checkbox for symbolic unit text. **Header unit (label only)** and **Applied units** distinguish CSV labels such as `[°C]` from declarations. Recognized headers can suggest a preset, but **Apply declaration** is required; no values are converted or calibrated. Temperature presets use `degC`/`degF` symbols. A unit is separate from the column's numeric/text type.

## Current capabilities

- Up to eight open sheet views; background CSV indexing and bounded asynchronous pages.
- Stable source/row/column identities; exact source text, declared types/units and Original/Effective views.
- Sparse nondestructive corrections/masks, limited calculated columns, Undo/Redo, correction files and materialized CSV export.
- 2D line/point figures, histograms, same-source overlays and dual axes; explicit row/key alignment for aligned scatter.
- Detached figure windows, adjustable axes/grids/appearance, linked table inspection and saved figures.
- Bounded 3D point clouds with camera controls and source-row inspection. Current rendering uses one billboard mesh, with at most 65,536 displayed points; it is not an Entities/Burst implementation.
- In 3D, **Show** toggles the source origin `(0,0,0)` and the X/Y/Z bounds-frame axes independently. Origin appears only within the displayed bounds. **Axis values** toggles source-coordinate tick labels on visible axes, with units at the high end; nearby labels are suppressed to reduce overlap. The setting persists with the figure and applies to annotated PNG exports. A yellow ring follows the selected visible point through camera navigation; selection is transient. **Near clip** defaults to `0.001` normalized preview units and accepts `0.00001–1`. These display settings do not rebuild the data; origin/axis visibility and near clip are saved with the figure. Annotated PNGs respect visibility; the live selection ring is not exported.
- PNG export for all five figure families. Each new `.fpfigure` folder contains `figure.png` and `provenance.json`; exports are limited to 4,194,304 pixels and do not overwrite existing products.
- 3D exports at 640×480 or larger include projected axes, source labels/units, viewport bounds and displayed/matching point counts. Smaller legacy exports retain the plain cloud image; provenance identifies whether annotations are present.

Large data is indexed/read in the background. Plot overviews and point sampling are display representations; source measurements remain unchanged. Resource reservations are estimates, not a measurement of total Unity/process memory.

## Disk quota: current status

A disk quota limits generated cache storage on the drive, not original file size or RAM. Old unused caches may be removed and later rebuilt from unchanged originals. Active caches must stay pinned.

`FPWorkbenchDiskCacheStore` implements owned entries, write admission, content manifests, pins and persisted LRU eviction. Additional generated artifacts can now receive their own reservation and extend an existing ownership manifest; unexpected or modified files prevent publication and remain protected. It does not adopt legacy directories.

`FPWorkbenchManagedCache` provides explicit runtime import/recovery with disposable source leases. Managed datasets route series, spatial, alignment and aligned-value products through additional-artifact admission. Workers and shared scalar-figure sessions retain independent pins. Workbook loading accepts this service to reconstruct absent caches, verify annotations/calculations/figures, and publish new in-memory cache references only after full admission; saving remains explicit.

Normal Editor imports, workbook opening, registered-source opening and detached-view verification now use the managed service. Adding another view of the same registered file verifies/reuses its cache. Tables and all detached figure families hold leases; cancellation/close releases them while independent worker pins survive to completion. An evicted workbook cache reconstructs from the original, with changed/missing originals refused before replacing the current workbook.

`FPWorkbenchCacheSettings` persists a local JSON root/quota configuration with a 4 GiB default and rejects roots inside Assets. The **Disk cache** foldout separates measured usage from estimated write reservations and offers eligible-cache trimming. Unexpected/modified files remain protected. Settings changes require idle views/workers. Live script reload with open real-file views has passed source reverification and cache-lock cleanup.

## Delivery verification

[DataWorkbenchPlan.md](DataWorkbenchPlan.md) is complete for the agreed delivery: disk-cache integration/recovery, a real whale-data viewer walkthrough, five inspected graph exports and final verification. Biologging integration, scientific equivalence, advanced spreadsheet features and additional platform work are outside this delivery.

Verified in Unity 6000.6.7f1: clean compilation and **170/170 Workbench Edit Mode tests passed**. The real 487,006-row CSV workbook recovered after deliberate isolated-cache eviction; all five plots regenerated from saved recipes. A subsequent trim reclaimed every generated artifact while preserving the source, workbooks and exports. The normal cache configuration is restored and the gallery workbook is open in Unity. See [delivery results](../../Documentation~/WorkbenchDeliveryResults.md) for artifact locations, timings, memory observations and limits.

Earlier detailed API notes and chronological progress logs are preserved in [the archived reference](../../Documentation~/Archive/DataWorkbenchReference.history-2026-11-12.md) and [archived plan](../../Documentation~/Archive/DataWorkbenchPlan.history-2026-11-12.md). They are historical context, not additional current requirements.

Plot styles also apply to cached overviews: Points draws min/max summary markers at bin centers using Point size; Line and Step retain extrema envelopes and add the corresponding first/last endpoint trace for clean bins. These traces summarize the bin, not every sample. Zoom in for individual samples. Embedded plots, Figure Windows and PNG exports use these conventions.
