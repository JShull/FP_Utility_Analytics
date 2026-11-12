# Data Workbench — initial slice

Open **FuzzPhyte > Utility > Analytics > Data Workbench**. Choose strict UTF-8 or Windows-1252, browse/paste a CSV path, then Add sheet. Import hashes the original and builds a disk-backed logical-record offset index in the background. It never rewrites the source. This first UI supports up to eight sheets and one import at a time.

Use the row-number field, arrows or mouse wheel to navigate. Columns share the available table width; widening the window or shrinking the sidebar reveals more columns, with a 110-pixel target minimum and a 32-column request cap. Single column arrows move one column; double arrows move one visible window. The arrow buttons are equal-sized and adjacent. Resize requests coalesce and stale-width pages are discarded. Only a 128-row page is retained by the view. The tinted **Row #** strip is the workbench logical record index, not source data. Drag its header edge to resize it, click its header toggle or use its context menu to hide it, and use **Show Row #** above the table to restore it. Its width/visibility are saved in the column-layout metadata. The toolbar label widths keep Columns readable. Drag a data-column header edge to set a manual width; double-click the edge or right-click it and choose **Reset to Auto** to restore that column only. Width preferences are saved in EditorPrefs per verified dataset identity and restored when the file is imported/reopened; other columns remain automatic. Cached cells redraw as pixel dimensions change, without waiting for a mouse-up event. Clicking a cell displays its exact source text in the footer and chooses that column for plotting. Integers are not silently rounded through double in the table. The table has a captured vertical scrollbar at its right edge, with a draggable thumb and exact one-row arrow buttons. It maps pixel motion to 64-bit row positions and streams the new page; dragging outside the track keeps capture until release. Source files remain read-only; the correction overlay below provides effective values.

Choose a Y column and an X binding (logical row, numeric column, or an explicit UTC date/time pair). Set the UTC format and gap threshold as needed, then **Apply binding / build plot**. This builds or verifies persisted points and a 256x4 min/max summary pyramid on a worker. Wheel zooms and middle-drag/Shift-wheel pans through cached views without reparsing CSV; manual X limits and Fit X are also available. UTC X is elapsed seconds from the declared origin shown beneath the plot. No implicit local-time or sensor-calibration conversion occurs.

Y grid spacing (0 = automatic), X grid spacing, grid visibility, Y lock/manual limits/inversion and a title are available. Overviews retain extrema; exact one-sample detail bins can connect as lines when contiguous and valid. Missing readings/gaps prevent connecting across them. Scatter/log axes, multiple series and cross-sheet alignment remain planned.

The sidebar and plot/table splitters capture drag input. Sheet references/cache paths, column layouts and applied binding/view settings survive window/domain reload; reopening verifies original source and index hashes in the background. Row navigation and adjacent, equal-sized column arrows occupy separate compact toolbar rows at the left. A strip above the table aligns hidden-column counts to its left/right edges and identifies the currently shown range.

**Open workbook… / Save workbook…** persists a versioned `.fpworkbook` JSON document containing workbook/sheet identities, source and cache references, import interpretation, table positions, manual column widths and applied plot bindings/view settings. Complete recordings are never embedded or overwritten; correction metadata retains only changed-cell originals and replacements as preconditions/provenance. Save takes a metadata snapshot and publishes it atomically on a worker. Open verifies every referenced source/index before replacing the current workbook; errors/cancellation leave it intact. Current paths are absolute: source relinking, cache rebuilding after cache deletion and relative/portable packaging remain planned. Keep original files and referenced caches available. Caches live in `%LOCALAPPDATA%/FuzzPhyte/Analytics/Workbench`; cache quota/cleanup UI is not implemented yet.

Runtime APIs in `FuzzPhyte.Utility.Analytics.WorkBench`:

- `FPWorkbenchCsvDataset.ImportAsync`: new cache directory, explicit CSV profile, cancellation/progress.
- `OpenAsync`: reopen a cache after source/index integrity checks.
- `ReadPageAsync`: 0-based source records and columns, at most 256 rows/32 columns and 4 MiB of selected UTF-16 cell text. Oversized pages fail explicitly; individual logical records have a separate configured byte bound.
- `Cell` / `FPWorkbenchRange`: source revision identity and 64-bit row coordinates, half-open ranges.
- `FPWorkbenchPlot.SummarizeAsync`: finite numeric row-domain extrema with original extrema row identities; invariant numeric parsing.

These services require no UnityEditor hooks and can run without an EditorWindow. No scene-stat, Entities or Burst dependency is introduced. Current CSV profile exposes delimiter/encoding/bounds programmatically; the first UI exposes encoding and assumes comma delimiters. Duplicate/empty header names retain distinct source column ordinals. Automatic type/unit/time inference is intentionally not implemented.

The next plot backend is available programmatically as `FPWorkbenchSeriesBinding` / `FPWorkbenchSeriesCache`: explicit row/numeric/UTC date-time X binding, numeric Y, source-row identity, gap policy and a persisted sorted-X point/summary pyramid. `BuildAsync` builds or checksum-verifies a cached series; `QueryAsync` reads bounded summaries/detail for an X range without reparsing the CSV. UTC uses an explicit format and tick origin with elapsed double seconds; duplicate times are retained. Decreasing/invalid X rejects the binding instead of sorting silently. This backend now drives the Editor plot controls. Cache quota/management remains open.

Data tests cover malformed/quoted/multiline CSV, BOM/legacy encoding, cell identity, source/index changes, cancellation/no-overwrite, precision retention, spike/invalid summaries and tick limits. The opt-in recovered-data test is local-only and does not redistribute private measurements. Further acceptance gates, including total memory/performance, Player/iOS and scientific comparison, remain in [the plan](DataWorkbenchPlan.md).

## Shared figure windows and reloads

Use **Open Figure Window** after applying a plot binding. The public Unity EditorWindow can float or dock through Unity. Views of a figure share one reference-counted runtime `FPWorkbenchFigureSession`: plot settings, cached series and latest queried view are shared, rather than copied per window. Closing a sheet/workbench view releases only its lease; a detached figure keeps its session alive. The final view cancels pending work. Reattach by reopening the same workbook/sheet figure; an explicit duplicate/figure-tab UI remains planned.

Editor reload serializes working sheet settings and each detached figure's metadata, not buffers or tasks. Verified cache references recreate the readers/controller. Working column metadata takes priority over local EditorPrefs defaults, so unsaved or workbook-loaded widths are retained. `.fpworkbook` remains the explicit durable document for reopening the analysis beyond the current Editor layout. Detached-window serialization/reopen, shared ownership and last-view cleanup are covered by tests; platform builds and total-process memory profiling remain separate gates.

## Non-destructive corrections

Select a data cell to inspect its original value in the sidebar. Choose a correction kind (text, finite number, signed 64-bit integer, boolean, missing or masked), enter the effective value/reason and **Apply correction**. **Restore source** removes that override. Corrected cells have a subtle highlight; masked cells show `[masked]`. The **Original values** toggle switches table/plot presentation back to the untouched source layer. These are explicitly typed corrections to source text, not automatic column-type inference.

Edits use stable dataset/row/column identity and original-cell preconditions. They never rewrite CSV or change row numbering. Corrections are shared across sheets referencing the same verified dataset revision. Unity Undo/Redo restores the correction metadata only; table navigation, plot range and source bytes remain unchanged. Affected figures rebuild their effective cache under the correction fingerprint; stale generations are not presented as the new layer.

`.fpworkbook` saves correction sets alongside workbook metadata. **Export changes…** writes a standalone `.fpchanges` file; **Import changes…** validates source revisions and original values against currently open datasets before replacing the corresponding source's correction set as one undoable action. A conflict, changed current edits during import, cancellation or unknown source leaves current corrections intact. Original/effective view state and unsaved corrections survive Editor serialization/reload.

This first edit slice supports 8,192 sparse cell corrections and at most 1 MiB of retained text per source, with a 2 MiB combined correction-metadata limit in the Editor. It fails explicitly at those bounds. Indexed large overlays, range/batch editing, added annotation columns, formulas/reductions, materialized CSV export and cross-sheet derivations remain planned. No insert/delete-source-row or silent timestamp/sensor recalibration operation is introduced.

## Calculated-column engine (runtime API)

`FPWorkbenchFormulaProgram.Compile(dataset, definition)` compiles bounded arithmetic using 1-based `C1` source and `D1` calculated-column references. Persist `program.Definition` to retain resolved stable reference IDs across renaming/reordering. Each worker owns an evaluator; call `SetRow` with original source values and the source row identity, then `Derived` for a result. An optional immutable correction snapshot supplies effective inputs without modifying those values.

Expressions support constants, parentheses, unary signs and arithmetic. Cycles, removed references, incompatible declared units and excessive expression sizes reject compilation. Evaluation returns explicit missing, nonnumeric, divide-by-zero, nonfinite and integer-precision statuses. Bare decimal integers beyond the exact double-integer boundary are rejected before conversion, including values outside Int64 range. Other numeric notation uses finite double arithmetic; this is not arbitrary-precision arithmetic. Unit declarations are explicit symbolic dimensions, with no automatic unit conversion.

The arithmetic engine is tested independently; table authoring, calculated-Y plots and source-owned workbook definitions are described below. `SUM` is not treated as a row expression or a visible-page sum; range reductions use the separate API below.

`FPWorkbenchCalculatedPage.ReadAsync` now provides a background tile of calculated results (up to 256 rows/32 calculated columns), reading all required source inputs independently of the currently displayed table columns. Results carry source/formula/correction revision identities and explicit error statuses. `FPWorkbenchReduction.CalculateAsync` sequentially reads a declared half-open row range and returns count, invalid count, compensated double sum, minimum, maximum and mean. Callers choose reject or skip for invalid inputs; an empty range has sum/count zero and NaN extrema/mean. Finite overflow fails explicitly. Both APIs support cancellation and retain only bounded row/tile state; no Editor API is required. Progress for reductions reports processed rows every 4,096 records and at completion. Tile/reduction fixtures passed after Editor recovery; these APIs are not yet exposed through the plot UI.

## Column dragging (initial staged workflow)

Drag a source-column header into the embedded plot area. While carrying the column, numeric X and Y assignment targets appear. Drop onto a target, then use **Apply binding / build plot** to display the staged binding. Column-edge dragging still resizes widths. Source/workbook/column revision identity is checked before assignment; a foreign source or mismatched ordinal is rejected. X assignments choose numeric X; use the sidebar for UTC date/time configuration. This first workflow replaces the single draft X or Y binding; it does not append an overlay series. Multi-series, detached-window drop handling and explicit cross-sheet alignment remain planned. Binding/source-validation tests pass; the native end-to-end drag gesture still needs interactive qualification.

**Apply binding / build plot** now records a dedicated binding Undo/Redo step. It restores the applied X/Y/time/gap binding (or the initial hidden plot state) without restoring table navigation or axis limits. Rebuilt series still follow the latest-request revision checks. Draft control edits are not individual Undo steps; apply commits them together. Undo state belongs to the workbench sheet and is cleared when that sheet is closed or its workbook is replaced. Detached figures share the live session while it remains attached; cross-window edit history after the owning sheet closes remains future work.

## Multi-series runtime foundation

`FPWorkbenchOverlay.BuildAsync` admits 1–8 distinct physical/calculated Y identities with one common row/numeric/UTC X binding and one correction snapshot. It clones input bindings/selections, reuses checksum-qualified scalar caches and builds missing caches sequentially to avoid simultaneous whole-file scans. This first backend deliberately requires one verified dataset; it does not infer cross-sheet alignment, unit conversion or clock transforms.

`QueryAsync` queries each bounded cache for the same viewport and publishes one `FPWorkbenchOverlayView` only after every query succeeds. A failure or cancellation produces no partial overlay. The result includes ordered column identities, source/correction revision, per-series row/UTC metadata and union Y extrema; concurrent requests return independent range-qualified results. Up to eight queries are allowed, each at most 4,096 requested bins. Whole-process cache quotas and one-pass multi-column cache construction remain future performance work. The multi-series window below uses this service; workbook-owned overlay documents remain pending.

### Multi-series window

Use **Open Multi-series Window** under the source Y selector. This public Unity window starts with that column and the sheet's draft X binding. Add distinct Y columns with the selector or drop a same-workbook/source column header onto the plot to append it (up to eight). Remove controls and per-series colors form the legend. All series use one shared row/numeric/UTC X binding and one unchanged original/effective correction snapshot. **Apply X** commits X/date-format controls; no implicit cross-source pairing or unit conversion is performed.

Wheel zoom and captured middle-button pan use the existing plot renderer. Auto Y uses union extrema across the series; Lock Y captures the current union range and exposes manual limits. Fit X uses the source domain; Fit Y captures the current visible union. Background cache admission/query work never runs from repaint. All-series publication and viewport checks reject stale pan/zoom results; an explicitly labelled previous viewport can remain while the next one loads. Source references, selections, colors, X/viewport settings and the frozen correction snapshot restore through Editor serialization/reload. Reopen from the workbench to take later cell corrections. Each window currently owns its overlay controller; shared overlay-view ownership, edit Undo and native gesture qualification remain pending; workbook persistence is described below. Rendering retains the scalar backend's min/max envelopes at coarse scales and contiguous lines at detail scale; unrestricted unsorted scatter is not yet implemented.

## Workbook-managed saved figures

Histogram and multi-series windows opened from a workbench now register stable figure IDs with that workbook. **Save workbook** captures open figures' controls and retains closed figures as metadata. Expand **Saved figures** in the sidebar to reopen a figure; selecting an already-open figure focuses it. The × action closes its view and removes its saved descriptor. Up to 32 figures are supported, within the existing 4 MiB workbook document limit. Existing workbooks without a figure list remain supported.

`FPWorkbenchFigureDocument` stores kind/version, source revision, bindings, selected columns/colors, plot or histogram controls, and an explicit `HasCorrectionSnapshot` flag plus optional frozen corrections. Runtime load verifies source schema/ranges and each correction's original-cell preconditions before admitting the workbook. Original figures do not acquire a fabricated empty correction set through Unity JSON serialization. Callers supplying corrections must set the presence flag; contradictory metadata rejects validation.

Replacing a workbook closes its histogram/multi-series views after the replacement sources have been verified. Restored figure views regenerate data products on demand; raw samples, buffers, tasks and file handles are never stored in the workbook. The source registry described below retains figure sources independently of open tabs. Single-series embedded figures keep their established sheet metadata path. Figure deletion/settings Undo, shared overlay controller ownership and portable source relinking remain future work.

## Registered sources and closed tabs

The workbook now has a persistent source registry (up to 64 sources) independent of its eight open sheet tabs. Source records contain the dataset revision, source/cache paths, import profile and display name; they contain no samples or open handles. Closing a tab retains this reference, its corrections and saved figures. A workbook with no open sheets can still save, verify all registered sources and reopen its figures. Older workbook documents acquire registry entries from their sheet references during validation.

Expand **Registered sources** to reopen a table from its existing verified index instead of importing the CSV again. In-memory dataset descriptors hold no file handles; after Editor reload, a closed-tab source is checksum-verified in the background when a figure or table needs it. Saved figures can open directly without recreating a sheet tab. Workbook file loading verifies all registry entries before replacing current state, including closed-tab source revisions. Cancellation or failed verification leaves current workbook metadata intact. Save-path protection covers all registered cache directories, not just open tabs. Source removal/relinking, closed-tab navigation-state retention and dataset-wide cache quotas remain future work.

## Saved calculation definitions and virtual table pages

A registered source can now store `HasCalculations` plus its `FPWorkbenchFormulaSet`. Set the explicit presence marker when supplying definitions. Save resolves and retains stable source/derived reference IDs and checks bounded grammar, units and cycles against the import profile's maximum schema size. Load recompiles against the actual verified column count before admitting the workbook. Source identity mismatches, missing definitions and contradictory presence markers reject validation. Definitions remain with the registered source when all sheet tabs close; refreshing the source descriptor in the Editor preserves them. Formula authoring controls are described below.

`FPWorkbenchTableView` presents physical source columns followed by calculated columns under one indexed schema. It supplies names, stable column IDs, source/calculated classification and cancellable mixed-page reads. Source cells retain exact original text, effective text and masks; calculated cells have explicit evaluation status and no invented original value. `SourceCell` refuses calculated edit targets. Tiles carry source/formula/correction revisions and are bounded to 256 rows, 32 visible columns and 4 MiB of retained cell text. Formula evaluation reads its full required source inputs even when they are outside the requested visible columns. The Editor grid now uses this adapter when calculations are present; physical-only sources retain their established page path.

### Authoring and viewing calculated columns

Expand **Calculated columns** in the source sidebar. Choose **New calculated column** or an existing definition, edit its name/expression/optional expected units, then **Apply calculation**. `C1` refers to the first physical source column and `D1` to the first calculated definition. Supports bounded arithmetic and dependencies, not spreadsheet macros or implicit full-column `SUM`. Expected units check explicitly declared source units; CSV header text is not treated as a calibration/unit contract. Remove rejects definitions still referenced by another calculation. Invalid expressions/cycles/units leave the previous definitions intact.

Calculated columns now appear after physical columns in the main table, with a tinted `ƒ` header and explicit error text where evaluation fails. They resize/page with the table; they cannot be selected as source-cell correction targets. Use the Y selector or drag the calculated header to the embedded Y target to plot them. Physical columns retain original-cell inspection/correction behavior. Definition changes use dedicated Undo/Redo metadata (up to 2 MiB in this Editor path), preserving table row navigation and unrelated plot state. Original/effective layer selection applies consistently to computed inputs. Page workers coalesce/cancel stale requests and publish only matching source/formula/correction revisions.

Widths of existing columns survive schema growth, with live calculated-column width remapping by stable identity. Saved workbooks validate virtual column positions and widths, including source-plus-calculated schema sizes up to 4,160 columns. Calculation definitions remain source-owned across tab close/reopen; formula editor controls are draft until Apply. Undo history is cleared when replacing the workbook. Public `CompileSchema` validates a bounded schema declaration (1–4,096 physical columns); recompile against a verified dataset before evaluating real rows. Calculated X and bulk/materialized export remain upcoming work.

### Calculated Y plots

The embedded plot's Y selector now includes calculated columns. Select one and **Apply binding / build plot**. The binding stores `YDerivedId` rather than depending on its current display ordinal; X remains a declared physical numeric/UTC column or source row index. Programmatic cache construction supplies the matching immutable formula program to `FPWorkbenchSeriesCache.BuildAsync(..., calculations: program)`; a missing program or removed ID rejects admission. Formula and correction fingerprints qualify the cache and each queried view. Changes to unrelated formulas do not invalidate physical-Y caches.

Calculated Y is evaluated from original source records with the correction snapshot before X/UTC corrections are applied to the parser's working record. This preserves original-cell preconditions when an input column also supplies X. Formula errors/masks become invalid samples, never invented zeros; table cells retain the detailed error status, while plot envelopes aggregate invalid counts. Formula edits rebuild a derived plot through the session's latest-request generation checks while retaining its viewport. Undo/Redo for formula and cell edits updates the curve consistently. Removing a calculated column used by an active plot is rejected until that plot is rebound.

Workbook load resolves calculated Y against the verified source definitions. Detached single-series figures serialize their formula snapshot and can reconstruct it after the owning workbench closes/reloads. Calculated X remains pending; histogram and multi-series viewers now accept calculated Y selections. The existing scalar sorted-X contract still applies; this is not unrestricted scatter or scientific processing equivalence.

Calculated `ƒ` headers now participate in column dragging. Drop onto the embedded plot's **Assign Y** target, then Apply the binding. The payload includes the source revision, stable derived ID and virtual column identity; the target resolves the current ordinal from that ID, so a definition reorder does not redirect the plot. Unsupported calculated-X, removed/foreign identities and mismatched physical/derived identities reject without accepting a new binding. Overlay targets require the same frozen formula revision before accepting derived payloads. Source/calculated column-edge resizing retains priority over header dragging. Automated tests exercise staging, rejection, stable-ID reorder resolution and the resulting curve; native end-to-end mouse drag qualification remains open.

### Calculated histograms

**Open Histogram Window** now accepts the selected calculated Y column as well as physical columns. It freezes the matching formula program and correction layer at open, labels the result as calculated and shows the formula snapshot. Extent fitting and bin counting evaluate that snapshot across the declared source row range in background passes. Later workbook formula/correction edits do not change this saved distribution; reopen a new histogram from the current Y selection to use the latest definitions.

Saved histogram figure descriptors carry an explicit calculation-snapshot presence flag, bounded definitions and a stable `HistogramDerivedId`. Workbook admission recompiles the frozen definitions against the verified physical schema and resolves the selected ID before admitting the figure. This remains independent of current source-owned calculations: removing or changing those definitions does not invalidate a saved histogram with its own valid snapshot. Missing IDs, mismatched revisions or invalid snapshot definitions reject load. Physical histograms continue through their established metadata path; calculated histogram drag/drop and settings Undo remain future work.

### Mixed physical/calculated multi-series figures

**Open Multi-series Window** now accepts a calculated initial Y selection and freezes the source's current formula program when available. Its Add Y selector includes physical columns and `ƒ` calculations from that snapshot. Up to eight distinct physical/derived identities may share the figure. Per-series colors, removal, common X navigation and union Y fit/lock work across the mixed set. Hover a calculated legend entry to inspect its frozen expression. Changes to workbook definitions or corrections do not mutate this snapshot figure.

Calculated-header drops append a series only when the workbook/source/stable ID and formula fingerprint match the figure snapshot. A newer formula revision prompts reopening the figure instead of silently mixing old and new calculations. The selector inside the figure always uses its frozen definitions. Duplicate, missing and foreign IDs reject append. End-to-end native mouse dragging remains a separate qualification item.

The runtime overlay API accepts parallel `yColumns` and optional `yDerivedIds` arrays plus `calculations`. Empty derived IDs select the physical column; nonempty IDs select a calculation (use a valid physical placeholder such as zero in the corresponding integer slot). All cache queries must return the same source/correction revision and, for calculated series, the same formula revision before a frame publishes. Saved figures persist the ordered stable IDs and a formula snapshot independently of current source calculations. Workbook load resolves them against the verified source schema. Cross-source alignment, per-series axis assignment and overlay-edit Undo remain pending.

## Histogram runtime service

`FPWorkbenchHistogram.CalculateAsync` scans a declared half-open source row range, accepting a source or calculated column and an optional immutable correction snapshot. Declare finite minimum/maximum and 1–4,096 equal-width bins. Bin intervals include their left edge and exclude their right edge, except the final bin includes the maximum. Stored double edges must be strictly increasing; collapsed bins reject configuration. A binary edge search avoids division-rounding misclassification at exact boundaries.

The result retains source/formula/correction identity, range, column, invalid policy and read-only edges/counts. Underflow, overflow and invalid rows are counted separately. Invalid inputs either reject the operation or are skipped under an explicit policy. `Density(bin)` normalizes over included samples and actual represented bin width; excluded values do not enter that denominator. Empty histograms have zero counts/density. Density overflow fails explicitly. The background scan supports cancellation/progress, keeps one source record plus bounded bin arrays in memory and never uses downsampled plot envelopes. The runtime histogram API requires explicit limits; the Editor viewer below can obtain limits through a background reduction. Persistent histogram caching remains pending.

### Histogram window

Choose the source Y column in the workbench sidebar, then **Open Histogram Window**. The public Unity EditorWindow can float or dock. It starts with a cancellable background extent scan followed by a histogram build. Choose 0-based first/exclusive-end rows, explicit minimum/maximum, bin count and invalid-input policy. **Fit limits / build** scans that declared range for limits; **Apply / build** uses the entered limits. Constant inputs get explicit padded display limits. Count/density and grid toggles change presentation without another source pass; hover shows bin boundaries and sample count.

The window explicitly holds the original/effective revision snapshot selected at open, including sparse corrections. Reopen from the workbench to incorporate later edits. Displayed row coverage, bin count and included/invalid/below/above counts describe the last successful result, which remains visible if a later configuration fails. Editor metadata retains the verified source reference, frozen corrections and controls through reload; readers/tasks/bar buffers are transient and regenerate in the background. Closing cancels work and observes worker errors. Shared cached histogram products, plot exports and histogram-setting Undo remain future work; workbook-managed figures are described above. No source measurements are modified.

### Calculated numeric X coordinates

Numeric X selectors and embedded X header-drop targets accept calculated columns by stable definition ID, including mixed-series figures. Row-index and UTC modes retain their existing meanings; UTC date/time parsing uses physical source columns. Applying a calculated X binding requires its matching formula program. Workbook and figure restoration resolve that ID against the admitted current or frozen definitions respectively.

X calculations use original records plus the declared correction snapshot, just like calculated Y. Coordinates must be valid, finite and nondecreasing; duplicates retain every source sample. Invalid formula results, decreasing coordinates and overflowing coordinate extents reject the build explicitly. Rows are never silently sorted or dropped to repair X. Formula revisions qualify every cache that depends on calculated X, including physical Y series in an overlay. Scrubbing and plot queries continue to use bounded indexed caches rather than evaluating the whole file on the Editor thread.

Validation: the connected Unity Editor compiled cleanly and completed 68 Workbench Edit Mode tests with no failures. Added fixtures cover corrected shared X/Y dependencies, duplicate X retention, formula cache revisions, overlay provenance and invalid/decreasing X rejection. Native OS dragging, large-data throughput, Player/iOS support and scientific equivalence remain separate qualification work.

### Line, step and point presentation

Plot settings now include Line, Step (hold the previous value until the next sample), and Points styles. Select the style in the embedded plot settings or a detached/multi-series toolbar. Embedded controls also set line width (1–8 pixels) and point size (1–16 pixels); those settings follow shared scalar views and persist in workbook/figure metadata. Older documents retain the default line presentation.

Styles operate on the existing sorted-X query backend. At individual-sample resolution, Line connects contiguous valid records, Step draws a horizontal hold and vertical transition, and Points shows unconnected square markers. Invalid records and declared gaps break connections. Coarse bins continue to show extrema envelopes, with an explicit zoom-in notice, rather than inventing individual points from an aggregate. This does not yet implement arbitrary unsorted scatter or source-order unsorted lines.

Validation: 69 Workbench Edit Mode tests passed, including style bounds, legacy JSON defaults and workbook round trips. A focused native EditorWindow test also cycled all styles without changing the viewport and restored the selected style/width/size after serialized window recreation. Visual quality and interactive native mouse qualification remain separate from these automated checks.

### Axis labels and detached figure controls

Embedded plot settings expose Title, X label and Y label. Custom axis labels are presentation text, so entering units does not convert measurements or establish unit compatibility. The renderer reserves space for the horizontal X label and rotated Y label; numeric tick labels and cache/UTC-origin information remain separate. Empty labels preserve the compact layout.

Detached scalar and multi-series windows offer a collapsible **Figure settings** section containing those labels, line/point controls and explicit X/Y grid spacing. The foldout survives window recreation. Scalar controls share their existing figure session with the embedded plot; overlay controls belong to that snapshot figure. Labels persist in workbook and figure metadata. Changing presentation does not move the viewport or rebuild source indexes.

Validation: clean Unity compilation and 69/69 Workbench Edit Mode tests, including updated workbook label round trips and overlay-window label/foldout recreation while preserving the panned range. Automated repaint coverage does not establish a manual visual-layout review at every window size.

### Figure-settings Undo

Embedded, detached scalar and multi-series figure controls now record presentation authoring through Unity Undo/Redo: title/axis labels, style, line/point size, grid/tick spacing, inversion and locked Y bounds. Zoom, X range, pan and automatic Y limits are excluded, so undoing an authored label does not return the viewer to an earlier location. Two views sharing scalar settings apply their changes in chronological Undo order; repaint-only synchronization creates no entries. No source rows or cache payloads enter the Undo object.

The lightweight Editor-only Undo proxies clear their own history when their owning window/tab closes or reloads, and when a workbook is replaced. Saved presentation values persist through the existing workbook/window metadata, but Undo history is session-local. Overlay series additions/removals/colors, figure deletion and histogram settings Undo remain open work.

Validation: clean compile and 70 Workbench tests passed, including shared-view Undo/Redo interleaving, unchanged panned range, preserved automatic limits and restored locked bounds.

## Materialized CSV export

Expand **Materialized CSV export** in a sheet sidebar. Choose a 0-based first/exclusive-end row range and a 1-based first column plus count (1–256 consecutive virtual columns). The current Original/Effective layer and calculation program are frozen when export starts. Choose a new `.fpexport` folder name; it contains `data.csv` and `provenance.json`, rather than a workbook or replacement source file. Cancel leaves no published product. Existing destinations are never replaced.

The runtime `FPWorkbenchCsvExport.WriteAsync` also accepts an ordered array of distinct virtual column ordinals, allowing nonconsecutive/reordered selections. It scans sequentially on a worker, retains one bounded source record and evaluator, writes strict UTF-8/comma/CRLF CSV, and quotes every field. Physical source strings preserve leading zeroes, large integer text, embedded quotes and line breaks. Effective masks produce empty fields. Invalid calculated results either reject the entire export or become empty fields with an explicit count, according to the selected policy; rows are never silently skipped.

Provenance records source identity/path/import profile, selected stable column IDs/names, half-open row range, formula/correction fingerprints and snapshots, invalid/masked cell counts, output conventions and the CSV SHA-256. It does not claim alignment settings for an alignment engine that is not yet implemented. CSV and metadata are staged in a sibling directory and published together by a no-overwrite directory move; cancellation/failure removes staging. Hashing is also chunked and cancellable. Atomic publication is an application-level filesystem operation, not a power-loss durability guarantee.

Validation: clean Unity compilation and 71/71 Workbench tests passed. Export fixtures reimport quoted/multiline CSV, verify exact source text and column order, original/effective values, masks, formula failure policy, provenance snapshots, empty ranges, protected paths, existing-product rejection, pre-cancellation and cancellation before publication. The source is unchanged and failed/cancelled staging is removed. Native save-dialog interaction and representative large-file throughput remain separate qualification work.

## Unsorted spatial point runtime

`FPWorkbenchPointCache.BuildAsync` accepts two or three virtual column ordinals (X/Y or X/Y/Z), immutable formulas/corrections and an explicit Reject/Skip invalid-coordinate policy. Coordinates remain doubles; every valid point retains its original long source-row index. Source order is preserved in storage, and no monotonic-X requirement or implicit sorting is imposed. Numeric conversion follows the formula engine's finite/precision rules, including rejection of integer coordinate text that cannot be represented exactly under that contract. Invalid-coordinate rows are counted; they are only omitted under the selected Skip policy.

The cache stores fixed-size points and per-4,096-point axis-aligned bounds on disk. Source/binding/formula/correction/policy identity determines the cache product; reopening verifies file sizes and SHA-256 payload hashes. Construction is cancellable and publishes through sibling staging. The runtime assembly adds no package or Editor dependency.

`QueryAsync` accepts inclusive finite XYZ bounds and a 1–65,536 point budget. It scans the disk-resident block index, skips nonintersecting blocks, and reads matching candidates without allocating the whole index or dataset. A deterministic source-row-hash heap bounds output memory; returned samples are ordered by source row and retain exact source mappings. `MatchingCount` counts all matching valid points, `Decimated` identifies sampled output, and `RecordsRead`/`BlocksRead` expose query cost. This sampling preserves identity and repeatability, not extrema or density equivalence. It must not be used as a full-data histogram/reduction.

This is a runtime foundation, not yet a workbench scatter or 3D Editor viewer. Worst-case unsorted data with overlapping block extents may require scanning every cached point; cancellation and bounded memory do not establish fast queries. A spatial hierarchy/LOD refinement and representative measurements remain open. GPU/Entities rendering, float local-origin preparation, point inspection and workbook-owned spatial figures also remain open.

Validation: clean compile and 73/73 Workbench tests passed. New fixtures exercise decreasing X, exact row mappings, block skipping, empty ranges, deterministic nested point budgets, source/calculated XYZ correction consistency, cache reopening/hash corruption, invalid policies, failed staging cleanup and query cancellation. Source recordings remain unchanged; no large-data throughput or scientific/3D rendering qualification is claimed.

## Unsorted scatter window

Choose **Open Scatter Window** in the sheet's Series binding section. The public EditorWindow can float or dock and opens a frozen source/formula/correction snapshot. Choose physical or calculated X/Y columns, the invalid-coordinate policy, and **Apply / fit**. Numeric embedded X bindings carry over; other embedded X modes start with the first physical column, so explicitly choose a numeric X in the scatter controls. UTC date/time interpretation and row-index pseudo-columns are not yet scatter bindings.

Drag a compatible workbench column header onto the X or Y selector to stage it; Apply builds the selected binding. Calculated drops require the matching frozen formula revision. Wheel zooms around the pointer; middle-drag pans both axes; Fit restores valid-point extents. Both axes remain double precision until normalized to screen pixels. A constant axis is padded for display; unrepresentable/overflowing display spans reject explicitly.

The point budget is bounded to 1–65,536. Coverage shows displayed versus matching points, sampling status, invalid source rows and cache records read. Click within ten pixels of a rendered point to inspect its original 0-based source row and full-precision XY coordinates. Inspection searches rendered samples, not every omitted point. The previous queried range stays visible with an update notice while a new background query runs; cancelled/stale query results do not replace the current view.

Figure settings include labels, grid/tick spacing, inversion, marker size and color. Presentation Undo uses the existing isolated proxy (color is not yet part of that Undo payload). Serialized window metadata restores bindings, snapshot definitions and viewport after recreation/reload; scatter windows also participate in the workbook Saved figures list (see below). Closing cancels workers and releases transient state. Linked table navigation/selection, multi-series scatter, arbitrary source-order lines, PNG export and the 3D renderer remain open.

Validation: clean compile and 74/74 Workbench tests passed. The native window fixture covers decreasing X, full versus sampled coverage, zoom queries, metadata recreation with retained viewport/labels, staged physical-column acceptance and foreign-source rejection. Native OS drag gestures, manual layout review and representative data/GPU performance remain unqualified.

### Workbook-owned scatter figures

Scatter figures now participate in the workbook **Saved figures** list alongside histograms and overlays. Opening or closing a scatter window retains its descriptor; workbook save captures current open-window presentation, applied XY bindings, point budget, policy, color and viewport. Unapplied staged axis selections do not replace the plotted binding in the descriptor. Selecting an already-open saved figure focuses it; closing/reopening recreates its verified cache/view in the background. Removing a figure closes the owned window; workbook replacement closes owned scatter windows as well.

Scatter axes persist physical ordinals or stable calculated IDs with independent frozen formula/correction snapshots. Workbook admission recompiles those definitions against the verified source and resolves both axes; missing IDs or foreign source references reject admission rather than selecting a fallback. Descriptors retain the previously admitted snapshot during background source reopening, so saving while verification runs does not dereference a missing dataset. Current source calculation definitions are not required when a valid frozen figure snapshot supplies them.

Validation: clean compile and 75/75 Workbench tests passed. Added coverage checks durable scatter kind/budget/color/viewport/calculated-ID round trips independently of current source definitions, unresolved-ID admission rejection, invalid budgets, and native saved-descriptor reopening while preserving the displayed range. Figure-deletion Undo, linked selection, shared spatial query ownership and 3D figures remain open.

### Reveal scatter points in source tables

After inspecting a rendered point, use **Reveal source row in workbook**. The owning workbook focuses or reopens that verified source sheet, navigates to the original row, tints the row and outlines the contributing physical cells. Calculated axes recursively resolve their frozen same-row arithmetic dependencies through stable formula references. The runtime `SourceDependencies` API returns a sorted distinct copy of those physical ordinals; definition reordering does not change their meaning. Constant-only formulas still reveal the source row even when they contribute no physical columns.

Navigation keeps the sheet's current Original/Effective layer, calculations and plot viewport. The status message explicitly distinguishes the table's current layer from the figure's frozen snapshot; no corrections or definitions are applied to make the views match. Revealing clears a previous editable-cell selection, avoiding an edit action against an unrelated old cell. If the tab was closed, the registered source is reopened from its verified index in the background; the eight-open-tab limit still applies. A closed/missing owning workbook or unregistered source is reported instead of routing to another workbook.

Validation: clean compilation and 76/76 Workbench tests passed. Fixtures verify nested/reordered formula source dependencies, independent returned arrays, source-row/column selection with unchanged original layer and X viewport, foreign-workbook rejection, and background indexed sheet reopening after clearing the admitted-source handle. Native point-click/button gestures and full table-to-figure bidirectional selection remain open qualification/features respectively.

## Local-origin point presentation preparation

`FPWorkbenchSpatialFrame.Fit` constructs a double local origin and one uniform `SourceUnitsPerLocalUnit` from spatial bounds. It preserves axis proportions and centers the largest extent near [-1, 1], avoiding direct conversion of large source offsets to Unity floats. A constant cloud uses unit scale. Midpoint construction avoids overflowing a finite extreme source extent; normalization divides by the source-unit scale rather than requiring a finite reciprocal.

`FPWorkbenchPointPreparation.PrepareAsync` prepares only a bounded query result (maximum 65,536 points) on a worker. It returns read-only Unity `Vector3` positions, corresponding long source rows, frame, revision identity, full matching count and measured errors. Original source doubles remain in the point cache/query, never replaced by these presentation positions. Source reconstruction is available through `frame.ToSource`, and overflow is rejected.

The caller declares a maximum absolute local-coordinate float error (default 1e-6). Each normalized component is compared with its float conversion; nonfinite conversions or excess error reject the batch. `MaximumSourceReconstructionError` separately measures the double source-coordinate round trip for the prepared samples. A local-space error bound is not a scientific source-unit accuracy claim: scale and double arithmetic affect source-unit reconstruction. Display sampling is also independent of this conversion error.

This bounded managed preparation is the dependency-light boundary for a later GPU/Burst/Entities rendering adapter. It creates no GameObjects/entities and makes no scene or Editor calls. No relevant point-rendering evidence exists under this package's absent `Research` directory. The host declares Entities packages, but this runtime assembly intentionally gains no Entities/Burst dependency here. Rendering adapter selection still requires measured upload, frame time and memory costs; no performance improvement or platform qualification is claimed.

Validation: clean Unity compile and 78/78 Workbench tests passed. Added fixtures demonstrate small deltas retained around a 1e12 origin where direct float casts collapse them, uniform-axis scaling, row/revision preservation, measured reconstruction error, inadequate-frame/zero-error-budget rejection, cancellation, constant-cloud handling and normalization of [-1e308, 1e308] without extent overflow. The 3D viewer/rendering adapter remains open work.

## 3D point preview

Choose **Open 3D Point Window** in the sheet's Series binding section. This public floating/dockable window freezes the source/formula/correction snapshot. Choose numeric physical or calculated X/Y/Z columns and Apply. The initial selectors use the first three available columns; date/text columns require an explicit numeric replacement. No calibration, axis-unit compatibility or trajectory reconstruction is implied by displaying XYZ.

The viewer builds/reuses the unsorted point cache, queries a bounded sample and prepares a uniform local-origin float batch in the background. It draws one billboard mesh, not a GameObject per point. The shared `FPMeshPreviewEditorUtility` supplies fit, orthographic/perspective projection, scroll zoom, orbit, pan and orientation widgets. Right-drag orbits; middle-drag pans; wheel zooms; Frame resets pan/zoom. Source-coordinate clip limits can restrict XYZ coverage after Apply. Invalid-coordinate policy, point budget (1–65,536), color and pixel size are explicit.

Coverage reports displayed/matching/invalid rows; diagnostics show worker pipeline time, last main-thread mesh upload time and actual local float error. These are observations for that refresh, not benchmark guarantees. Click a rendered point to inspect its original double XYZ and source row, then Reveal to navigate to physical contributors in the owning table. Occluded/omitted point picking and multi-series 3D selection are not implemented. Cancelling an update keeps the previous successful batch; closing cancels workers and releases mesh/material/preview resources.

`FPWorkbenchPointRenderer` is a runtime Unity adapter accepting an explicitly supplied supported billboard shader. It owns one mesh/material, uses four centered vertices and six indices per point, and uploads only when a prepared batch changes. At the maximum budget this is 262,144 vertices/393,216 indices plus bounded preparation arrays; representative allocation/upload profiling is still required. Devices lacking 32-bit index support reject budgets above 16,383. The shader uses vertex-expanded pixel-size circles without geometry shaders. Runtime/Player callers must retain/reference the shader asset explicitly to avoid build stripping; Editor `Shader.Find` success is not Player qualification.

Window metadata restores source snapshot, XYZ selectors, clipping and camera controls after recreation. The 3D viewer participates in workbook Saved figures (below); Undo for its bindings/camera/appearance remains open. GPU procedural/instanced rendering and a Burst/Entities adapter remain options to evaluate against this measured baseline; no new package dependency was added and no advanced adapter is claimed complete.

Validation: clean Unity compile and 79/79 Workbench tests passed. The native 3D fixture verified shader import, actual non-background billboard pixels via RenderTexture readback, mesh/index counts, projected-point source-row inspection, window metadata recreation and destruction of owned mesh/material on close. Native interaction usability, other render pipelines, Player/IL2CPP/iPad, large-data/GPU profiling and workbook-owned 3D figure persistence remain open.

### Workbook-owned 3D figures

3D point figures now share the workbook Saved figures lifecycle: capture on save/close, focus existing views, reopen from admitted or background-verified sources, remove and close on workbook replacement. Descriptors store applied XYZ axes as physical ordinals/stable derived IDs, independent frozen formula/correction snapshots, applied point budget/invalid policy/clip limits, current appearance and camera pan/zoom/orientation/projection. Unapplied axis/clip/budget drafts do not replace a successful displayed batch's descriptor.

Runtime admission validates the verified source schema and all calculated XYZ references, normalized point color, budget, clip bounds, pixel size, finite bounded local pan/zoom and a unit camera quaternion. The camera data model uses UnityEngine values and no Editor types; orthographic mode is a boolean in durable metadata. Current source calculation definitions can be absent when a valid figure snapshot supplies the frozen definitions. A retained descriptor remains available while the window verifies its source; transient meshes/materials/previews are rebuilt rather than serialized.

Validation: clean compile and 80/80 Workbench tests passed. Added workbook fixtures verify frozen calculated Z independently of current source definitions, camera/clip/budget round trips and unresolved-Z/invalid-camera/clip rejection. The native rendered-window fixture additionally reopened a saved descriptor with its camera pan/zoom and point count retained. Undo, additional axis/grid annotation, shared multi-view spatial budgets, GPU/Burst/Entities comparisons and Player/hardware qualification remain open.

## Dual Y axes for multi-series figures

Each multi-series legend row now selects **Left Y** or **Right Y**. Every curve uses the limits and inversion of its assigned axis; automatic limits combine only valid series assigned to that side. An empty side has no ticks. The left axis retains the main plot controls. When any series uses Right Y, the viewer reserves a right margin and exposes independent right label, inversion, grid/tick spacing, automatic/locked limits and Fit right Y. Fit operations use the assigned data extent even when current limits are locked. Source values/units are never converted by axis assignment.

Axis changes reuse the existing coherent cached data view and do not rebuild/requery it. Assignments and right settings persist through window/workbook descriptors. Legacy figures default all series to Left Y. Adding a series defaults it to Left Y; removing one preserves the remaining series' sides. Invalid saved assignment shapes or right settings reject workbook admission.

Side assignment has dedicated session-local Undo keyed by stable physical/calculated series identity. Undo does not restore removed series or change unrelated later series, data bindings or viewport navigation. Right presentation controls use the existing presentation Undo proxy. History clears with the owning window; values persist through metadata. Users are responsible for assigning compatible physical quantities to each axis; automatic unit grouping/conversion and cross-source alignment remain open.

Validation: clean Unity compile and 80/80 Workbench tests passed. Strengthened fixtures verify independent assigned extents, locked-versus-fit limits, one-sided assignment, legacy defaults, malformed assignment rejection, side Undo/Redo without navigation, native dual-axis repaint/recreation and workbook persistence. This completes the initial shared-source dual-Y presentation path, not multi-source alignment, logarithmic scales, arbitrary source-order line queries or unit-aware axis qualification.

## Source-unit declarations

Expand **Source units** in the sheet sidebar, select a physical column and apply a declaration. Empty text/clear means unspecified; `1` means dimensionless. Symbolic expressions support products, division and integer exponents such as `m/s^2`. Declarations are case-sensitive symbolic identities, not a unit conversion catalog: `m` and `cm` are different tokens, and temperature offsets/scales are not inferred. Headers expose canonical declared/inferred units in tooltips; labels in source CSV headers are not silently parsed as authoritative declarations.

Applying uses the existing bounded calculation-definition transaction and Undo/Redo. All dependent formulas and active calculated plot references validate before commit. An incompatible declaration leaves previous definitions intact and reports the error. Clearing returns the column to unknown units; it does not assert dimensionlessness or convert data. Expected-unit checks may reject clearing when a calculation requires a known result. Workbook source definitions, frozen figure snapshots and export provenance already retain these declarations through their existing metadata paths.

`FPWorkbenchFormulaProgram.SourceUnits(column)` returns the canonical symbolic declaration or `unspecified`; derived `Units(column)` remains available separately. Unit expressions enforce at most 16 independent symbols; cancellation of a symbol frees a slot. Per-token exponent and expression-length bounds remain in the parser. Source-unit editing does not change source strings, correction values, row navigation or plot viewport. Existing snapshot figures retain the units declared at their own open/save revision.

Validation: clean Unity compilation and 82/82 Workbench tests passed. Added fixtures cover native source-unit authoring, incompatible/malformed rejection with retained definitions, clear/Undo/Redo and inferred units, preserved row/viewport, workbook round trip, unchanged source bytes and the 16/17-symbol boundary. Automatic axis unit grouping, conversions/calibration, source-schema inference and scientific equivalence remain open.

## Cross-sheet alignment runtime foundation

`FPWorkbenchAlignment.BuildAsync` now implements the first two explicit pairing contracts. **SharedSourceRows** requires identical verified source revision, row count, schema and import profile; equal-length unrelated sources never qualify. **ExactKey** merge-aligns one physical key column from each source under a declared common `KeyDomain`. Both effective key streams must be nondecreasing; decreasing keys reject rather than being silently sorted or positionally paired.

Choose ordinal text or exact decimal keys. Text is case-sensitive ordinal comparison with no trimming or date interpretation. Decimal keys compare representable decimal values without binary floating-point coercion; alternate exact spellings such as `4` and `4.000` match. Parsing additionally verifies representation exactly, rejecting rounded precision and underflow. Decimal keys require matching known symbolic unit declarations (`LeftUnits`/`RightUnits`); these are caller declarations, not inferred facts from headers. Large identifiers above 2^53 therefore remain distinct when within decimal range. Use text for identifiers outside that range. Missing/masked/invalid keys reject.

Duplicate policy is Reject or ReportAmbiguous. ReportAmbiguous excludes every row in a duplicate-key group and every row on a matching opposite group; duplicate groups without an opposite match are also classified ambiguous under this uniqueness contract. It never emits Cartesian matches or chooses a tie silently. Remaining unique absent keys are unmatched. Matched + unmatched + ambiguous equals the source row count independently on each side.

The bounded background merge retains one pending key/group counter per source and streams matched long source-row pairs to a revision-qualified cache. Pages contain at most 4,096 pairs. Metadata records policy/domain/units, source identities/paths, coverage counts, correction fingerprints and frozen correction snapshots, plus a pair-payload checksum. Products publish through sibling staging and validate provenance/coverage/checksum on reopening; failures/cancellation clean staging. Corrections that reorder keys reject; new correction revisions get new products. This is mapping data, not a materialized cross-sheet calculation or resampled plot.

The mapping service is exposed in the cross-sheet alignment window described below; cross-sheet value/plot bindings remain open. Independent multi-source overlays, pairing UI/workbook descriptors, composite UTC date/time keys, external sorting indexes, explicit clock transforms, nearest/interpolated policies and aligned figure/export/inspection adapters remain open. A common-domain declaration alone does not establish clock synchronization or scientific equivalence. Split UTC date/time fields can now use composite UTC keys as described below; arbitrary transformed/timezone/clock keys remain explicit future work.

Validation: clean compile and 85/85 Workbench tests passed. Added fixtures cover matched/unmatched/ambiguous conservation, duplicate rejection, exact IDs above 2^53, corrected key revisions, bounded mapping pages/cache reopening, shared-row identity versus unrelated equal-length files, ordinal IDs, unsorted rejection, decimal rounding/underflow/missing-key rejection, incompatible unit declarations, corrupt payload rejection, cleanup and cancellation. No representative large-data throughput or scientific synchronization claim is made.

## Cross-sheet alignment window

Use **Open cross-sheet alignment…** under Registered sources. The public floating/dockable window receives registered source references and a frozen copy of current correction sets. It verifies selected source identities/paths/import profiles in the background before presenting physical key selectors. Choose SharedSourceRows or ExactKey; exact matching requires explicit key kind, common domain and duplicate policy, with declared symbolic units for decimal keys. **Use frozen corrections** selects the captured effective layer. Build/verify runs in the background and can be cancelled.

The result identifies its last successful source pair, layer/domain/key kind and matched/unmatched/ambiguous counts. Controls may be changed without replacing that result; a failed/cancelled operation retains it. The pair preview pages at most 128 mappings and provides Reveal left/right row actions into the owning workbook's registered tables. Verification/read/build failures appear in-window. Source admission is sequential and transient file readers are released; closing cancels and observes pending tasks.

Serialized window metadata restores the last successful request and pair-page location, rebuilding verified products rather than serializing pairs. Unapplied drafts do not replace that restored request. Source references retain frozen calculation definitions for value selection; key matching still uses physical columns. Correction snapshots remain frozen even if caller edits later change. This window is not yet a workbook-owned saved alignment document, and matched X/Y scatter generation is described below. Workbook-owned saving of that configuration remains open; independent multi-source overlays remain distinct from alignment.

Validation: clean compilation and 86/86 Workbench tests passed. The native fixture verifies background source admission, exact-key coverage/pairs, invalid-unit failure retaining the prior result, frozen correction revisions, updated effective coverage and metadata recreation with the previous pair-page location. Native row-reveal gestures, workbook-owned alignment persistence, aligned value/plot generation and representative throughput remain open.

## Generate an aligned cross-sheet scatter plot

In the alignment window, build/verify a mapping, then choose **X from left source**, **Y from right source**, an invalid numeric-value policy, and **Open aligned scatter plot**. These Editor selectors list physical columns and calculated columns from each registered source, using independently frozen definitions captured when alignment opens. Generation always uses the last successful mapping and its frozen correction layers, not unapplied key/layer drafts. Empty mappings cannot generate a plot. This is explicit paired-value plotting; independent overlays with separate X coordinates remain a different operation.

The runtime `FPWorkbenchAlignedValues.BuildAsync` also accepts calculated virtual columns and independent frozen programs. It streams matched row pairs and advances each source reader sequentially, retaining bounded row/evaluator state. A derived four-column CSV keeps exact long left/right source-row IDs alongside numeric XY. Invalid values either reject generation or produce empty coordinates under the explicit policy; the latter retains the pair row and the scatter cache skips invalid coordinates with coverage counts. Formula/dependency and full alignment/correction provenance are sealed with checksums. Source recordings and alignment mappings are never modified.

Derived data/provenance publish together through staging; their CSV index is built afterward and can be regenerated if cancellation interrupts indexing. Short flat `av-`/`ai-` cache folders avoid the Windows nested-path failure found in testing; full identity hashes remain in verified metadata. This is not a general long-path portability guarantee. Reopening validates the mapping, derived CSV/provenance seal and exact index source/profile. No whole dataset is retained in RAM.

Aligned scatter windows show mapping coverage and both frozen layers, retain zoom/pan and expose separate **Reveal left source row** / **Reveal right source row** actions. A bounded background row-ID read resolves the original rows; key columns and physical value/formula dependencies are highlighted in the owning workbook. Ordinary scatter windows use an explicit presence marker so JSON reload cannot manufacture an aligned-origin context.

Generated plots participate in workbook Saved figures through recipes referencing both original sources. Derived CSV/index paths are caches, never registered as original workbook sources. Aligned line/histogram/3D views, independent multi-source overlays and aligned exports remain open.

Validation: clean Unity compilation and 88/88 Workbench tests passed. New runtime fixtures verify paired original rows, calculated X and corrected Y, preserved source files, index/cache reopening, invalid/empty/cancelled products, mapping/provenance corruption and bounded point-cache handling. The strengthened native alignment fixture generates an actual scatter window and checks its XY values, match coverage and original-source context. Representative performance and full native gesture/visual qualification remain separate gates.


## Saved aligned scatter recipes

Aligned scatter figures now save their original left/right dataset identities, stable value-column identities, applied alignment policy, invalid-value policy and independent frozen calculation/correction snapshots. Reopening verifies both registered original sources and regenerates missing mapping/value/index products in the background. Saved viewport, point budget and appearance remain attached to the figure. Current source formulas and later correction edits do not replace its frozen inputs. Script reload also rebuilds from the recipe rather than requiring the temporary derived CSV to survive.

Both original sources must remain available and match their verified revisions/import profiles. Missing derived products can be regenerated; corrupt products are rejected rather than silently repaired. This does not implement original-source relinking or portable source paths.

Validation: 89/89 Workbench Editor tests passed. The new workbook fixture deletes derived mapping/data/index products, reloads a recipe containing calculated X and corrected Y, and verifies regenerated values retain the frozen inputs. It also checks missing-source and actual-schema rejection. The strengthened native window fixture reopens an aligned scatter after deleting derived products and checks point count and saved viewport. Representative large-data throughput, native gesture qualification and target-platform validation remain open.


## Local recovered-data TDR qualification

The explicit `RecoveredCsvQualifiesBackgroundIndexAndDistantPage` fixture now builds a UTC time/depth series (date column 0, time column 1, depth column 18) from the locally recovered Windows-1252 deployment CSV. It checks the declared UTC origin, full overview row conservation, bounded overview records and exact first-three source depths/2.5 ms timestamps. The private source stays outside the package and temporary test products are cleaned afterward.

Direct asynchronous execution through the connected Unity Editor passed on 2026-11-11. The local file contained 487,006 rows / 134,268,638 bytes. One cold temporary-cache run measured 5,209 ms indexing, 4,919 ms TDR cache build and 306 ms combined overview/detail queries; the overview read 1,940 cached records and emitted 1,903 bins. These are single-run local observations, not throughput acceptance thresholds, UI responsiveness measurements, biological interpretation or MATLAB equivalence. The standard Pipeline test filter returned zero selections for the explicit fixture; qualification therefore invoked the fixture directly with setup/teardown, and observed completion.


### Calculated aligned values in the Editor

The alignment X/Y dropdowns include each source's calculated columns, labeled Calculated. Source definitions are copied when the window opens, validated against the admitted schema and retained through window reload. Later edits to workbook formulas do not replace these captured definitions; reopen alignment to capture a new revision. Key selectors remain physical columns under the existing sorted-key contract. Generated Saved figures retain stable calculated identities and frozen definitions in their recipes.

Validation: clean Unity compilation and 89/89 Workbench tests passed, including the native alignment-window fixture with calculated X, external formula mutation isolation and saved-plot cache regeneration. This adds no dependency or source-data mutation.


## Reusable 2D presentation presets

In Figure settings, use Load/Save presentation preset to share a `.fpplot` JSON file across scalar, multi-series and scatter plots (also available in the sheet plot controls). Presets contain title, X/Y labels, line/step/point style, widths/sizes, grid visibility, Y inversion and X/Y tick spacing. They preserve the target viewport, automatic/locked Y limits, data bindings, correction/formula revisions and dual-axis assignments. This intentionally separates reusable presentation from a workbook's complete saved figure. Controls reuse the current presentation Undo transaction. Histogram, right-axis-specific controls, series colors and 3D cameras are not included.

The runtime `FPWorkbenchPlotPreset` codec has no UnityEditor dependency. It validates format/version, numeric limits and bounded text before applying anything. Editor file actions accept at most 16 KiB and write through a same-directory temporary file with atomic publication; file reads/writes occur only on explicit button actions. Existing preset paths may be replaced after the native save dialog. Invalid files leave presentation unchanged and report an error. Native file-dialog interaction remains a manual qualification gate.

Validation for presentation presets: clean Unity compile and 90/90 standard Workbench tests passed, including round-trip Unicode labels/style/ticks, preservation of target navigation/locked limits, invalid-application atomicity and unrelated/oversized-file rejection. Native file-dialog interaction has not been automated.


## Recover moved sources and missing indexes

Use **Recover moved/missing workbook sources…** below the workbook buttons. This opens a separate recovery window and leaves the active workbook intact. Choose the saved `.fpworkbook`, select an original source, and choose its identical CSV at the new location (or its current location to rebuild a missing index). Hashing, parsing and index construction run in the background and can be cancelled. Repeat for other missing sources, then save a new recovered workbook copy. Open that copy normally to admit all source revisions, formulas, corrections and figure bindings before displaying data.

Recovery reads structurally validated metadata only; that read never authorizes data display. A replacement must retain the saved content/import identity. Changed recordings are rejected rather than inheriting old annotations. A successful operation changes only detached source/sheet paths and cache references, retaining workbook/sheet/figure/cell identities, calculations, corrections and plot settings. It builds a new cache under the normal local Workbench cache parent, leaving old caches and recordings unchanged. Failure/cancellation cleans caches created by the failed operation. Accepted rebuilt caches remain available even before saving the copy; cache quota/abandoned-cache management remains open.

The recovery tool refuses to overwrite its input workbook. Saving the recovered copy does not claim that other unchanged sources have been verified; normal workbook opening remains the full admission boundary. Relinking a changed recording, automatic directory search, portable relative paths and live-session source migration are not implemented. Native chooser gestures remain a manual qualification gate. Runtime `ReadMetadataAsync` is explicitly unverified with respect to sources; `FPWorkbenchSourceRecovery.RelinkAsync` returns a detached snapshot, never mutates its input, and uses no UnityEditor APIs.

Recovery validation: clean Unity compilation and 93/93 standard Workbench tests passed. New fixtures cover moved identical content with changed timestamp/deleted old cache, preserved workbook/source/sheet/figure identities and corrections, changed-content rejection and cancellation cleanup. A native window fixture checks background metadata load, failed-read candidate retention and input-workbook overwrite rejection/copy saving. File chooser gestures and real-user recovery remain separate from these tests.


## Column projection foundation

`FPWorkbenchColumnProjection` stores order/visibility by stable schema identity rather than display position. Explicit schema binding preserves surviving identities, removes deleted definitions and appends new columns. Resolve returns the current source/calculated ordinals in displayed order. Hiding the last visible column is rejected. This model does not move source values, formula references or plot bindings.

`FPWorkbenchTableView.ReadProjectedPageAsync` reads up to 256 rows and 32 distinct arbitrary virtual columns in one indexed source pass, retaining requested order, original text, correction state and calculated status. Page `Columns` identifies each displayed value's actual virtual schema ordinal; callers must use that mapping rather than `FirstColumn + displayIndex`. The contiguous ReadPageAsync API now delegates to the same implementation and also supplies Columns. Input selections are copied before scheduling and retained page text remains bounded to 4 MiB.

The Editor integration described below now uses projected paging when columns are hidden or reordered; default identity layouts retain the existing contiguous physical-page path.

Column projection validation: clean Unity compilation and 95/95 standard Workbench tests passed. Fixtures cover stable reorder/hide/schema transitions, invalid/foreign identities, last-visible-column protection, mixed calculated/corrected arbitrary-column pages, input-array mutation isolation, duplicate/out-of-range/page bounds and contiguous compatibility.


### Organize visible table columns

Right-click a column header to move it left/right or hide it. The **Visibility…** menu beside column navigation restores individual hidden columns or resets display order. At least one column remains visible. Header dragging still assigns columns to plots; ordering uses explicit menu actions. Widths stay attached to source/calculated identities and paging counts reflect visible columns. Hiding/reordering changes neither source data nor plot/formula bindings.

Display order/visibility persist in each sheet's workbook layout, unsaved window metadata and the existing per-source local column preferences. Runtime workbook admission verifies saved projection identities against the actual physical/calculated schema. When calculation definitions change in the live workbook, surviving column identities retain order/visibility/widths; deleted definitions disappear and new definitions append. Invalid local preference projections reset safely rather than blocking the grid.

Column layout actions have session-local Undo/Redo without restoring row navigation or plot viewport. Linked figure-to-table reveal temporarily restores hidden contributing source columns as an undoable layout change and navigates to their display position. Transient pages carry actual schema ordinals through headers, resizing, source inspection, calculated selection and drag-to-plot payloads, so display positions never retarget edits. The grid suppresses stale pages during layout changes and retains the bounded background tile limits.

Editor projection validation: clean Unity compilation and 96/96 standard Workbench tests passed. The native window fixture verifies projected values, real cell-click source targets, layout Undo/Redo, workbook projection reload and reveal of hidden contributors; runtime tests also verify mapped manual-width fitting. Native context-menu gesture paths and broad large-schema interaction/performance qualification remain separate.


## Select and copy table ranges

Click a cell to start a selection, then Shift-click another cell to extend over the intervening visible columns and source rows. Selection highlighting uses stable physical/calculated column IDs. Reordering or hiding columns afterward does not retarget the selected IDs; copying still includes the original selection until it is cleared or replaced. A hidden/removed anchor cannot extend a new range: click a visible cell to restart. Up to 32 columns can be selected; row spans may cross table pages.

Use **Copy TSV** or Ctrl/Cmd+C outside text editing. The Headers toggle includes current column names. Copy uses the current Original/Effective layer and calculated definitions captured at the request. Source strings remain exact; masks appear as `[masked]` and calculation errors retain their explicit status text. Tabs, quotes and multiline text use quoted TSV escaping. This is a textual clipboard product, not a portable correction/provenance document.

Copies are background operations limited to 256 rows, 32 columns and 524,288 UTF-16 characters (1 MiB of character payload, excluding object/OS overhead). Bigger selections require the existing materialized CSV export. Invalid/oversized/cancelled operations never publish partial clipboard text. Closing/replacing the workbook cancels pending copies. Tests restore the user's previous clipboard afterward.

Each sheet persists selected row bounds and stable column IDs in workbook/window metadata; admission verifies source revision, row bounds and actual calculated schema. Reload uses the selection's first row/column as the new Shift anchor. Live removal of a selected calculation clears the invalid selection instead of binding it to a different column. Selection is navigation state rather than an Undo action. Drag selection, multiple disjoint ranges, table-to-figure range highlighting and drag selection remain open; selection-driven file export is described below.

Runtime `FPWorkbenchTableSelection` and `FPWorkbenchTableClipboard` have no Editor/OS clipboard dependency; only the Editor adapter publishes completed text.

Range/clipboard validation: clean Unity compilation and 98/98 standard Workbench tests passed. New tests verify reordered selection copying through the actual Editor clipboard, saved selection IDs, multiline/tab/quote escaping, frozen corrected/calculated values, masks, source preservation, foreign-ID rejection, row/text limits and cancellation. Native Shift-key/Ctrl-C gestures, interoperability with external spreadsheet applications and drag selection remain separate qualification work.


### Export the actual table selection

Expand **Materialized CSV export**, enable **Use table selection**, choose the invalid-calculation policy and use **Export table selection…**. The exporter resolves selected stable column IDs against the current verified schema and streams those columns in the selection's captured order across its full source-row span. The current Original/Effective layer and calculated definitions are frozen when export starts. Later selection or display-layout changes do not retarget the requested product.

This uses the existing staged materialized-export engine: a new `.fpexport` folder contains `data.csv` and provenance with source/column identities, row bounds, correction/formula snapshots and a payload checksum. It never overwrites source recordings or existing output products. The clipboard's 256-row/text limit does not apply to streamed CSV output; the table selection still supports at most 32 distinct columns. Manual configured export remains available for 1–256 consecutive schema columns, explicitly independent of visible order.

The native window fixture exports a reordered, nonadjacent selection, changes the selection after scheduling, reimports the generated CSV and verifies both rows, their original requested column order and provenance bounds/IDs. Native output-file chooser gestures remain a separate gate.

Selection-export validation: clean Unity compilation and 98/98 standard Workbench tests passed after integration. This extends the native projection fixture rather than adding a redundant test for the existing streaming writer.


## Composite UTC keys for cross-sheet alignment

Choose **UTC Date Time** key comparison in the alignment window, then select a physical date and time column on each source and declare their exact formats and common clock domain. Key comparison uses integer 100 ns Gregorian UTC ticks, never epoch values cast to float/double. Date/time fields are trimmed and joined with one space for parsing. Both streams must already be nondecreasing; duplicate and missing/masked-key policies remain explicit. Frozen corrections apply to both date and time fields.

Supported dates are `dd.MM.yyyy`, `yyyy-MM-dd`, `MM/dd/yyyy`, `dd/MM/yyyy` and `yyyy/MM/dd`. Follow the date with one space and `HH:mm:ss`, optionally a period/comma plus 1–7 required `f` fractional digits. Examples: `dd.MM.yyyy HH:mm:ss.ffffff` and `yyyy-MM-dd HH:mm:ss.fffffff`. Formats must specify a complete four-digit-year date; timezone tokens, optional fractional digits and culture-dependent/current-date defaults are rejected. Invalid dates, excess source precision and decreasing corrected timestamps reject rather than round, reorder or silently exclude values.

The policy and both source-column/format declarations persist in applied window state and workbook aligned-scatter recipes. Date and time columns are both retained among the original-source contributors for linked inspection. Existing ordinal/decimal enum values are unchanged. Expanded policy metadata participates in cache identities, so old derived products may be rebuilt without changing the original recordings.

Scientific contract: these are exact software timestamp comparisons under the user's UTC/common-clock declaration. Zero-tick equality is the acceptance criterion for the hand-enumerated synthetic fixtures; 100 ns representability is not sensor accuracy or clock synchronization. No offset, drift, timezone conversion, leap-second model, uncertainty window, nearest match or resampling is inferred. Independent MATLAB/reference comparison and deployment clock qualification remain required for scientific equivalence claims.

UTC-alignment validation: clean Unity compile and 102/102 standard Workbench tests passed. The native UTC fixture was then strengthened and passed again with swapped right-source date/time column positions. Coverage includes midnight/date-format transitions, adjacent 100 ns instants, exact pair identities, correction revisions, duplicate ambiguity, masked dates, invalid/decreasing/excess-precision rejection, contributor provenance, recipe regeneration and applied-window restoration. No independent sensor-clock or MATLAB equivalence validation is claimed.


## Frame a table selection in its scalar plot

After generating the sheet's scalar plot, use **Frame selected table rows** in Plot view. This reads the selected source-row endpoints from the plot's current sorted-X cache in the background, then navigates the shared scalar view to their X extent. Bound columns, data layers, formulas, line/point style and locked Y limits are retained. Pop-out scalar figure views share that viewport. Automatic Y behavior still follows the existing plot settings.

This is X navigation, not sample filtering or a change to the plotted Y column. Equal-X samples outside the selected source rows can remain visible, and single/equal-X extents use the existing Fit padding. The runtime `ReadRowExtentAsync` reads one or two cached point records regardless of selected range length, verifies their source-row identities and endpoint X values, and never reparses CSV or allocates selected-row arrays. Unrepresentable viewport padding rejects without replacing the current range.

Pending requests are discarded if the selection's row bounds, source series/binding or viewport changes before completion. Closing/replacing the workbook cancels pending work. This initial table-to-plot path is for the current sheet's scalar/shared figure session; selection highlighting/filtering in scatter, histograms, overlays and 3D views remains separate work.

Selection-framing validation: clean Unity compilation and 103/103 standard Workbench tests passed. Runtime coverage checks endpoint read counts, duplicate X values, source-row bounds and tampered endpoint identity. The native window fixture checks selected-row framing, retained locked Y limits/inversion/binding and rejection of a result superseded by a later pan.


## Histogram settings Undo and last-successful persistence

Histogram row bounds, bin counts, numeric limits, invalid-value policy, density and grid controls now participate in session-local Undo/Redo. Restoring a different query cancels/observes pending work and rebuilds in the background from the window's frozen source/formula/correction snapshot, retaining the previous bars until success. Density/grid-only restoration reuses the current result without querying source data. Fit limits records its completed limit change as an Undo step; rebuilds caused by Undo do not create additional history.

The window separately records its last successfully published histogram query. Saved figure descriptors and reload use those published row/bin/limit/policy values, rather than invalid or unapplied drafts. Current density/grid presentation still persists. A failed zero-bin draft therefore cannot poison the saved figure while valid bars remain displayed. Closing/reloading clears transient Undo history and workers; current valid settings survive through metadata. This does not add source-edit Undo or mutable data to the snapshot viewer.

Histogram Undo validation: clean Unity compile and 103/103 standard Workbench tests passed. The strengthened native histogram fixture verifies bin-count and limit Undo/Redo, fit-limit Undo, no-rebuild density restoration, frozen correction revisions and saving/reloading the last successful query after an invalid draft.


### Shared deterministic UTC parsing for plots and alignment

Scalar UTC series and composite alignment now use one runtime complete-date validator/parser. Series binding admission and workbook save validation reject missing-year/current-date formats, unsupported timezone tokens and precision beyond seven required fractional digits. Supported full formats are the same as the composite UTC section above; Editor UTC-format fields include the contract in tooltips. Existing full-date default `dd.MM.yyyy HH:mm:ss.ffffff` remains supported. Previously accepted custom UTC declarations outside this explicit format set must be corrected before a new series/workbook can be admitted; original source fields are never rewritten.

Alignment keys compare integer ticks. Scalar plots retain the integer UTC origin but use double elapsed seconds for X, so plotting coordinates are not a replacement for integer timestamp identity at arbitrary durations. The fixture checks a one-tick offset near the origin without casting full epoch ticks to double. Shared parsing removes culture/current-date inference; it does not establish device-clock agreement or scientific equivalence.

Shared-UTC validation: clean Unity compile and 104/104 standard Workbench tests passed. New coverage checks complete-date admission, one-tick relative-time offsets and rejection before cache/workbook publication. The explicit recovered 487,006-row TDR qualification also passed again through direct asynchronous connected-Editor execution with the shared parser. This verifies software intake/query correctness for that local file, not deployment clock calibration or MATLAB equivalence.


## Axis-scale numerical foundation

`FPWorkbenchAxis` provides double linear/Log10 normalization, inverse mapping and bounded ticks in original physical units. Limits must be finite/increasing, log limits positive, and transform inputs inside the declared domain. Callers must explicitly clip/exclude out-of-range/nonpositive data rather than rely on silent clamping. Only inverse roundoff at representable endpoints is clamped to the declared bounds.

Logarithmic tick spacing is in decades; auto spacing bounds the returned tick count. Tick values remain positive and strictly increasing after representability filtering. Narrow intervals may have few/no ticks for an explicit spacing. The transform uses relative-log and small-delta expansions to preserve narrow ranges at large offsets, and avoids overflow for broad positive ratios. Exact endpoints are retained; interior coordinates remain subject to double quantization. These are presentation transforms and do not rewrite measurements or timestamp identities.

Scalar, shared-source overlay/dual-Y and scatter Editor views now expose independent linear/Log10 scale controls. Dual-Y sides retain separate scale choices. Drawing, picking, ticks and navigation use the declared transform; log zoom/pan operates in logarithmic space. Saved metadata, presentation presets and Undo preserve scale choices, while legacy documents default to linear. Scatter limits can be entered explicitly. Invalid or nonpositive log limits report an error rather than silently selecting a positive subset or modifying measurements.

For Log-Y scalar summaries, bins with nonpositive minima are omitted and line continuity breaks. Mixed/nonpositive envelopes show an inline notice to zoom for individual samples. A mixed bin can contain positive observations, but its cache does not retain the minimum positive value; the whole bin is omitted rather than inventing that value. Histogram/3D logarithmic scales are not implemented.

Integration validation: 108/108 standard Workbench Edit Mode tests passed in the connected Editor. Fixtures cover logarithmic navigation anchors, underflow rejection, cached mapping, constant-domain expansion, preset rejection without partial mutation, scale persistence, Undo and native scalar/scatter/dual-axis repaint paths. These checks do not establish pixel-level visual acceptance, scientific equivalence or Player/device qualification.

## Right-Y presentation presets

For overlays with a series assigned to the right axis, expand Figure settings to load/save a right-Y `.fpplot` preset. Loading uses only the preset's Y label, scale, tick spacing, grid and inversion. It retains destination Y limits/lock, shared-X controls, series appearance and axis assignments. Existing presentation presets are accepted, and presets saved here remain ordinary `.fpplot` files; the right-Y load action deliberately ignores their other fields. Left and right presentations are loaded separately rather than as a combined figure recipe.

Locked nonpositive limits reject a Log10 preset before any settings change. Application participates in the right axis's presentation Undo/Redo. Native file-dialog interaction remains unqualified.

Validation: clean connected Unity compilation and 110/110 standard Workbench tests passed, including destination preservation, atomic refusal and right-Y preset Undo/Redo.

## Histogram presentation presets

Histogram windows offer Load/Save histogram preset actions using versioned `.fphistplot` JSON, bounded to 1 KiB. These presets contain only density/count mode and grid visibility. They exclude source/correction/formula identities, row ranges, bin counts, numeric bounds and invalid-value policy. Applying a preset uses the displayed histogram result without rebuilding or reading the source, and participates in settings Undo/Redo. The shared Editor file helper performs strict UTF-8 reading and staged replacement; histogram and ordinary plot preset formats reject each other. Native OS dialogs remain unqualified.

Validation: clean connected Unity compilation and 111/111 standard Workbench tests passed, including malformed/foreign/oversized preset rejection and native histogram application/Undo/Redo retaining the same result without a rebuild.

## Frozen-column runtime contract

Column projections can retain up to eight frozen visible stable IDs. `ResolveWindow` returns those columns in display order followed by a bounded window of non-frozen columns, without duplicate source ordinals. Its `first` parameter indexes only non-frozen columns; it is not the existing Editor's visible-column offset. A window is limited to 32 columns and must leave one scrolling slot when non-frozen data exists. Hiding a column unfreezes it; schema transitions remove deleted IDs and retain surviving identities. Older projection JSON defaults to no frozen columns.

The Editor now integrates this contract. Right-click a column header to freeze/unfreeze it. Frozen columns are tinted and remain on the left while column navigation pages the non-frozen data. Their displayed widths share at most half the table width while scrolling columns exist, contracting on narrow windows without rewriting saved manual widths. Navigation reports scrolling counts plus the frozen count. Hiding a frozen column unfreezes it; header actions participate in projection Undo and save with column preferences/workbook metadata.

Projected page reads, stable source-cell actions, Shift-click ranges and linked source-row reveal use the frozen viewport mapping. For frozen views, a Shift-click anchor outside the current resolved window must be restarted with a visible cell. Frozen mappings are cached until the projection/offset changes. Native context-menu interaction and a full domain-reload UI session remain manual qualification items.

Validation: 112/112 standard Workbench tests passed for the initial contract, followed by 3/3 focused projection tests after strengthening legacy/bounds and projected-page coverage. The latter verifies frozen calculated/source column resolution with corrections and unchanged source bytes.

Editor integration validation: clean connected Unity compilation and 112/112 standard Workbench tests passed, with the native window fixture extended for pinned page contents, width budget, Undo/Redo, range order, linked reveal and workbook metadata round trip.

The strengthened native fixture also passed a serialized-window recreation check: working metadata is captured, the original window is destroyed, a new window deserializes it, reopens the cached source and displays the same frozen columns/scrolling offset with expected source values. This focused test passed after clean compilation. It exercises serialization/lifecycle restoration without forcing a project-wide domain reload; actual domain-reload and manual context-menu/grip qualification remain separate.

## 3D appearance presets and Undo

Expand Appearance presets in a 3D point window to load/save `.fp3dplot` JSON (version 1, at most 2 KiB). Presets carry finite RGBA [0,1] and point size [1,32] pixels. They exclude camera orbit/pan/zoom/projection, input bindings, correction/formula snapshots, clipping and point budgets. Applying a preset changes appearance on the existing renderer without rebuilding point data. Color/size controls and preset application participate in appearance-only Undo/Redo; transient Undo state is disposed with the window. Figure/workbook snapshots retain the resulting appearance as before. Native file-dialog interaction remains unqualified.

Validation: clean connected Unity compilation and 113/113 standard Workbench tests passed. Coverage includes preset format/bounds, native application and Undo/Redo with renderer/camera/query preservation, saved appearance and existing rendered-pixel/resource-release checks. These are software checks, not scientific or device qualification.

## Explicit-size 3D PNG export

The 3D Appearance presets foldout includes PNG width/height and Export PNG. Choose a new `.fpfigure` output folder: it receives `figure.png` and `provenance.json` together through sibling staging and no-overwrite publication. Source files and prior outputs are retained. The image renders the current completed point snapshot and appearance with a separate camera at the requested aspect ratio, without resizing the window or changing its camera. It contains the point visualization/background, excluding Editor controls, labels and orientation gizmos.

Dimensions are 64–4096 per axis with at most 4,194,304 pixels total; PNG bytes are bounded to 32 MiB and provenance to 4 MiB. Rendering/readback is an explicit main-thread capture action deferred outside OnGUI; temporary graphics resources are released before worker encoding/publication (see the background-service integration below). These are allocation/work bounds, not measured latency guarantees. Export is disabled while a point update is active.

Provenance records dimensions, PNG SHA-256 (base64), source identity/path, completed figure snapshot, matching/rendered counts and sampling status, local origin/unit scale and maximum local conversion error. It describes the sampled view, not a full-resolution replacement for the source dataset. 2D/histogram PNG export, publication labels/legends, cancellation and manual file-dialog qualification remain open.

Validation: clean connected Unity compilation and 113/113 standard Workbench tests passed. The native point-preview fixture decodes the exported 640×480 PNG, checks colored pixels, verifies provenance/hash and preview camera/active-target preservation, and rejects existing destinations and oversized images. Graphics teardown checks remain green.

## Histogram PNG export

Histogram windows now expose Image export with explicit dimensions. A separate offscreen renderer draws the completed histogram's bars, linear axes, ticks, grid choice, count/density label and source-column title using Unity meshes/text; it reads no source rows. Export uses the same new `.fpfigure` folder convention with PNG and provenance staged together, never overwriting existing output. Provenance includes exact completed edges/counts, inclusion/exclusion totals, the published figure query/snapshots and a PNG SHA-256. Invalid draft bin/bound controls do not replace the published query in the export.

Bounds: width 320–4096, height 240–4096, at most 4,194,304 pixels; PNG 32 MiB, provenance 4 MiB. Rendering/readback is an explicit main-thread capture action deferred outside OnGUI; encoding/publication uses the background service below. Capture is disabled during a histogram update. No new external dependency is added. Fonts use Unity's built-in font; arbitrary glyph coverage, long-title layout and publication typography need further qualification. Scalar/overlay/scatter images and cancellable/background encoding remain open.

Validation: clean connected compile and 113/113 Workbench tests passed. The native histogram fixture decodes a 640×480 image, checks bar/label pixels, query provenance and no-rebuild behavior, and rejects overwrite. A generated Depth (m) histogram was also visually inspected after correcting its label scale. This does not establish image parity with MATLAB or broad export/platform acceptance.

## Scalar figure PNG export

Floating scalar figure windows expose Image export using the same explicit-size `.fpfigure` PNG/provenance convention (320–4096 width, 240–4096 height, at most 4,194,304 pixels). The image uses the completed cached view: it preserves line/step/point presentation, extrema envelopes, gaps, inversion, linear/log axes, declared limits, ticks, units/labels and UTC-origin information. Enlarging the export does not reread or increase the cached sample resolution. The shared offscreen image canvas now serves histogram and scalar images, with bounded meshes/text and deterministic cleanup.

Export refuses an active/failed query or a range whose cached view is stale. Log envelopes follow the viewer's existing nonpositive/mixed-bin omission policy; aggregation/omission counts are explicit in image notices and provenance. Metadata includes source-sheet/binding/settings, frozen correction/calculation snapshots, displayed-bin count, UTC origin and PNG hash. Image errors leave the figure controls available. Rendering/readback remains on the main thread, with bounded worker encoding/publication; manual dialogs, overlay/dual-axis/scatter images, arbitrary glyph/long-label layout and cancellation remain open.

Validation: 114/114 standard Workbench tests passed, then 2/2 focused figure tests passed after isolating export errors from source errors, with clean compilation. Native export fixtures decode all three styles with log Y and verify metadata/no-rebuild/stale-range refusal. A controlled pixel fixture distinguishes a genuine gap from a connecting segment and checks mixed log-bin omission. A generated inverted depth profile was visually inspected. This does not establish pixel parity with other software or scientific/platform qualification.

## Scatter PNG export

Ordinary and aligned scatter windows now expose Image export through the shared bounded offscreen canvas. Images retain completed-view bounds, independent linear/log scales, inversion, markers/color, ticks and labels; edge markers are clipped to the plot. Export refuses active/failed or stale range/budget queries and never increases sample resolution implicitly. The `.fpfigure` image/provenance pair uses the same dimension/byte limits, bounded worker encoding/publication and no-overwrite staging as scalar export.

Provenance records sampled/matching/invalid totals, exact displayed row IDs in the view dataset, applied bindings/settings/point budget and PNG hash. An unapplied invalid-value-policy draft does not replace the completed cache policy in saved/exported figure descriptors. Aligned exports also retain both original source references, coverage information and the frozen alignment recipe; displayed rows identify the derived view, not original source ordinals. The references/recipe provide the connection to both originals. Camera/query state is unchanged, and image errors leave controls usable.

Validation: clean connected Unity compilation and 114/114 standard Workbench tests passed. Native fixtures decode sampled scatter image dimensions/pixels, verify sampled rows/totals and applied-policy provenance, retain the existing view, and verify both original references/recipe for aligned exports. A generated scatter image was visually inspected. Overlay/dual-axis image export, long-label/glyph qualification, cancellation, manual dialogs and device/performance acceptance remain open.

## Overlay and dual-axis PNG export

Overlay Image export now preserves all completed shared-source series, colors, line/step/point styles, envelopes/gaps and independent left/right axis settings. The scalar and overlay paths share series rendering. Images include side-specific ticks/labels and a color-keyed legend marked L/R, with reserved footer space for up to eight entries. A right-only assignment does not require a valid unused left log domain. Invalid active-axis log limits refuse output; source measurements are unchanged.

Export requires a completed coherent viewport and uses the existing cached bins. New `.fpfigure` folders contain PNG/hash and figure provenance including source/correction/formula snapshots, series IDs/colors/side assignments, both axis settings, per-series bin counts, UTC origin and aggregate/log-omission indicators. Dimensions are 440–4096 width, 360–4096 height, at most 4,194,304 pixels; prior PNG/provenance byte bounds and no-overwrite staging apply. Rendering/readback remains explicit and main-thread bound; encoding/publication uses the background service below.

Validation: clean connected Unity compile and 114/114 standard Workbench tests passed after sharing the scalar/overlay renderer. Native fixtures decode all three styles with log-left/linear-right axes, verify saved side/scale/color metadata and unchanged view, cover right-only assignment, and reject an invalid right log domain without output. A generated dual-axis image was visually inspected. Initial PNG paths now exist for scalar, overlay/dual-axis, histogram, ordinary/aligned scatter and 3D; full export acceptance still requires typography/long-label/glyph layout, cancellation, native dialogs and platform/performance qualification.

## Background image publication service

`FPWorkbenchImageExport.PublishAsync` is a runtime service with no Editor references. It takes a caller-captured RGBA8 `Color32[]` and a plain serializable metadata subclass of `FPWorkbenchImageProvenance`. It snapshots both inputs before returning a worker task, encodes PNG, hashes it and stages PNG/provenance together in a new `.fpfigure` folder. Only one service job is admitted per application domain; another request fails before copying its inputs rather than queuing another large buffer. Limits are 4096 per axis, 4,194,304 pixels, 32 MiB PNG and 4 MiB JSON. The owned pixel clone is at most 16 MiB; the caller's readback buffer and graphics resources remain separate responsibilities.

The worker uses Unity's documented [thread-safe array PNG encoder](https://docs.unity3d.com/zh-cn/engine/6000.6/script-reference/unityengine/imageconversion/encodearraytopng) and [background-capable JSON serialization](https://docs.unity3d.com/ja/current/ScriptReference/JsonUtility.ToJson.html). Payloads must be plain data, with no Unity objects or main-thread callbacks. No new packages are required; there was no relevant package/project Research folder for this engineering change.

Cancellation is checked before/after native encoding, during 64 KiB writes and before folder publication. An in-progress native encode cannot be interrupted; cancellation after the publication commit does not retract a valid output. Failed/cancelled jobs clean their staging directory and release admission. Results report worker thread ID, encode/publish durations and byte count for later measurement; no latency/performance acceptance is claimed.

Validation: clean connected Unity compilation and 117/117 Workbench tests passed. New fixtures verify off-caller-thread execution, decoded RGBA pixel orientation/content, caller-buffer/metadata mutation isolation, exclusive admission, precommit cancellation cleanup, successful subsequent admission, bounds/metadata refusal and no overwrite. Editor adoption is now implemented as described below. These tests establish worker behavior; target-size end-to-end latency remains unqualified.

## Editor worker integration and cancellation

All scalar, overlay, histogram, scatter/aligned-scatter and 3D export actions now use `FPWorkbenchEditorImageJob` and the runtime worker. Main-thread rendering/readback captures the current completed view; the texture/temporary graphics are released after the worker snapshots the pixels/metadata. PNG encoding, hashing and staged file publication then run off the Editor thread. Existing output formats and provenance fields remain intact through the shared runtime provenance base.

One active image worker is admitted across these windows. Other export buttons disable during admission; the job adapter also checks before capture to reject queued button callbacks. Active jobs show status and Cancel image export even when the export foldout closes. Completion reports main-thread capture, worker encode and publication timings, with an explicit Show exported image folder action. Errors/cancellation retain usable figure controls and do not change the data/plot/camera.

Closing a window or beginning script reload cancels the owned token, detaches Editor callbacks and observes any worker fault without blocking the main thread. An encoder already executing finishes its native step before cancellation is observed; an already committed pair remains valid. Pending jobs are cancelled rather than resumed after reload. Rendering/readback and request cloning still require main-thread time; this is not an end-to-end responsiveness or latency guarantee.

Validation: clean connected Unity compile and 120/120 standard Workbench tests passed. Existing native image fixtures now await worker tasks and recheck all exported plot families, pixels/metadata/hash and retained view/camera settings. New adapter tests verify worker completion/status metrics, disposal cancellation/callback detachment and actual figure teardown cancelling its owned pending job. Actual project-wide domain reload, manual buttons/dialogs and large-image/target-device profiling remain acceptance work.

## Explicit typed-schema foundation

`FPWorkbenchTypedSchema` declares physical columns by verified dataset ID, stable column ID and ordinal. Compiling validates those identities and freezes per-column parsers plus a definition fingerprint. Bounds are 4,096 declarations and 1 MiB serialized definition; each column has at most 16 missing tokens of at most 64 characters. Units are metadata only, without implicit calibration or conversion. The fingerprint conservatively includes declaration details, not just mathematically equivalent interpretation.

Types are text, signed/unsigned 64-bit integer, finite double, boolean and complete UTC timestamp. Integer parsing retains the exact declared integer range without a double conversion; larger identifiers should remain text. UTC uses the existing complete-date format validator and integer ticks, with explicit UTC interpretation and no inferred local zone/current date. Real parsing uses an explicit named culture (invariant by default), decimal/exponent syntax and no thousands separators. Booleans use explicit true/false tokens. Whitespace trimming and ordinal case-insensitive token comparison are opt-in.

Empty fields are not automatically missing: declare an empty missing token where intended. Actual null input is missing. Otherwise missing tokens, invalid input and valid values stay distinct. A mask changes the effective state while retaining raw field text, parsed state and underlying typed value for inspection. Numeric fields are meaningful only for their matching type and valid parsed state. Framework double parsing is a declared floating-point representation, not an exact decimal/identifier contract.

Validation: clean connected compile and 124/124 Workbench tests passed. Fixtures cover signed/unsigned limits and >2^53 integer IDs, missing/invalid/masked distinctions, explicit culture/whitespace/grouping/boolean policies, nonfinite rejection, one-tick UTC differences, invalid dates/formats, definition snapshot isolation, fingerprints, foreign schema refusal and unchanged source bytes.

This is a runtime parsing foundation. Source-document persistence/admission is now implemented below; authoring, formula/plot/alignment/export interpretation and schema-qualified numeric cache revisions are not wired yet. Typed edit admission is described below. Typed table classification is implemented below. Existing raw tables and numeric products retain their current behavior; no typed scientific pipeline acceptance is claimed.

## Typed schemas in saved source metadata

Registered sources can now retain an explicitly present typed schema (`HasTypedSchema`/`TypedSchema`). Source validation rejects contradictory presence markers or foreign dataset revisions and snapshots bounded declarations. Workbook admission compiles them against the verified physical source schema, so a structurally valid document with foreign column IDs cannot be admitted. Old source records without a declaration retain their prior behavior; absent declarations are not inferred.

Declarations survive closed-sheet workbook saves, Editor source registration refreshes and content-identical moved-source recovery. Alignment windows copy declarations with their source metadata and verify both sides, including a shared-cache alias, without claiming that the alignment algorithm consumes the types yet. The raw dataset identity remains separate from the typed definition fingerprint; changing interpretation does not rewrite recordings.

Validation: clean connected Unity compilation and 125/125 standard Workbench tests passed. Fixtures verify closed-sheet save/load/fingerprints, actual-column mismatch rejection, presence contradictions, moved-source preservation, Editor refresh retention and alignment-source snapshot isolation. Typed authoring/numeric-consumer/cache-revision integration is still required before users can rely on these declarations for analysis. Table classification is implemented below.

## Typed virtual table pages

`FPWorkbenchTableView` optionally accepts an immutable compiled physical schema. Declared physical cells retain both original and effective typed classifications alongside unchanged raw/display strings and stable source IDs. Undeclared and calculated cells retain their existing contracts; calculated values still report formula status rather than an invented physical declaration. Typed page fingerprints are separate from source/formula/correction revisions.

Explicit Missing corrections override token parsing to the missing state. Masks preserve the original field's parsed state/value beneath the masked effective state, even though the legacy display layer emits empty text for masked cells. No classification rewrites the source or rounds an integer through double. Pages remain bounded to 256 rows/32 columns and the existing retained-text allowance; optional typed-cell objects add bounded fixed payload and are not included in that text-only budget.

Editor table construction/page requests now carry the compiled schema and reject stale typed revisions, including when formulas, projections or frozen columns are also active. Missing/invalid declared cells have distinct tints and type/state tooltips; their literal field text remains displayed. Original/Effective view selection continues to choose which correction snapshot is read. Standalone readers classify malformed supplied corrections as invalid rather than coercing them. Editor/workbook typed correction admission is now enforced below; authoring and numeric consumers remain pending.

Validation: clean connected compile and 126/126 Workbench tests passed. Runtime fixtures cover projected exact unsigned IDs, explicit decimal culture, original/effective corrections, masked invalid source values, explicit missing quality, boolean values, schema-revision changes, untyped legacy behavior and source immutability. The native source window fixture verifies typed-page publication with the expected schema revision/value. Formula/plot/alignment/export numeric interpretation still requires schema-aware integration.

## Typed correction admission

Compiled schemas validate proposed source-column overrides and whole correction sets. Editor commits, including imported changes, validate all candidate corrections against the current declared source before mutation or Undo registration. Undo/Redo refuses a now-invalid target under the current schema while retaining the last applied state. Workbook admission and source recovery also validate active corrections after source/precondition verification. This does not reject invalid raw measurements: those remain inspectable and may be masked or marked missing.

Ordinary values must parse as valid or explicitly declared missing tokens. Missing/Masked remain explicit quality actions for every type. The correction storage kind retains its existing contract too: Number/Integer/Boolean use their legacy invariant/signed/bool formats; choose Text input for declared decimal cultures, custom boolean tokens or full unsigned values. Text storage does not bypass declared type validation. Values are never silently reformatted or converted.

Validation: clean connected Unity compile and 127/127 standard Workbench tests passed. Fixtures reject unsigned overflow/negative overrides and invalid real/boolean tokens, accept declared tokens/quality actions, reject invalid active workbook corrections and verify Editor rejection leaves corrections unchanged plus valid Undo/Redo preserving row/viewport. Source immutability checks remain green. Schema authoring and frozen-figure/formula/plot/alignment/export schema snapshots/cache revisions remain open; those numeric consumers are not yet type-aware.

Axis foundation validation: clean Unity compile and 106/106 standard Workbench tests passed after reopening the authorized matching Editor. New fixtures cover exact endpoints, representative inverse round trips across the positive double domain, four-ULP ranges near 1e300, overflow-safe linear normalization, major/explicit/bounded log ticks and invalid-domain/spacing rejection. The representative round-trip checks use a 1e-12 normalized tolerance and narrow-range fraction checks 1e-14; they do not establish universal error bounds for quantized interior coordinates or any rendered log-view behavior.

### Typed formula snapshots

`FPWorkbenchFormulaProgram.Compile(dataset, formulas, compiledSchema)` freezes explicit physical types, decimal culture, missing tokens and units into the returned `Definition`. Restoring that definition retains its parsing contract and fingerprint. Existing untyped definitions retain legacy parsing. Declared text, boolean and UTC values do not implicitly become arithmetic numbers; integer magnitudes beyond 2^53 report IntegerPrecision. Quality Missing/Masked inputs propagate Missing. Conflicting known formula/schema units reject compilation. Invalid typed tokens report NonNumeric.

Point caches using a typed program include its fingerprint even for physical-only coordinates. Histograms already read through program evaluators. This API does not automatically attach registered source declarations to every Editor formula/plot/alignment path; that integration and schema authoring remain pending. Frozen historical definitions must retain their own snapshot rather than silently adopting current declarations. Adding snapshot fields can regenerate old formula-qualified caches safely.

Validation: connected Unity compilation and 130/130 standard Workbench tests passed, covering culture/quality/units, exact integer boundaries, snapshot isolation/roundtrip, contradictory metadata, boolean/UTC refusal, legacy parsing and physical scatter cache separation.

### Typed scalar cache inputs

When supplied a typed formula program, `FPWorkbenchSeriesCache.BuildAsync` reads physical numeric X/Y through that program's evaluator and includes its fingerprint even without derived axes. Invalid/non-numeric Y becomes a missing observation; invalid numeric X fails the build rather than inventing coordinates. Correction preconditions apply to original field text before declared parsing. UTC date/time axes continue using the explicit SeriesBinding format/date-column contract. Untyped physical axes retain existing behavior. The Editor's source-wide schema propagation is a separate remaining integration task.

Physical figure sessions now invalidate on a changed or removed typed schema, even when no calculated axis is selected. An unchanged program is a no-op. Verification: 131/131 standard tests passed after scalar correction/failure cleanup checks; subsequent session invalidation checks passed with clean compilation and 9/9 focused series tests. The blocking NUnit async assertion used during development was replaced with direct awaited exception checking; the affected Editor was restarted and validation completed.

### Editor typed-program propagation (2026-11-12)

Active sheet schema construction now compiles registered physical declarations into its table/plot program even when no calculated columns exist. New scalar/overlay/scatter/3D windows receive that program through existing paths. Physical histogram creation now retains it as well, including its saved figure snapshot; reopening retains the saved interpretation. Applying calculation definitions validates against the active source declarations before committing. Historical frozen figure definitions are not replaced with current declarations.

Validation: clean connected compilation; 132/132 standard Workbench tests passed, including native typed physical histogram count/fit and saved reopening. A subsequent 1/1 focused Editor fixture verifies physical-only source propagation. Remaining work includes schema authoring/Undo, aligned products, admission consistency and UTC contracts. These notes supersede earlier active-sheet/histogram propagation deferrals only.

### Typed aligned values (2026-11-12)

Alignment-window admission now compiles independent registered type declarations into each side's value program, including a source with no calculated columns. Existing aligned value fingerprints and recipes retain those program snapshots, so deleting and rebuilding products preserves declared cultures and missing tokens. Key matching continues to use the explicit alignment key policy; no implicit type conversion, unit conversion or resampling is introduced.

Connected Unity compilation and 133/133 standard tests passed. Coverage includes independent de-DE/invariant values, missing fields, deleted-product regeneration from immutable recipes and native alignment-window snapshot/export/reopen checks. Typed schema authoring and broader key/admission contracts remain pending.

### Authoring column declarations

In the Workbench sidebar, expand **Column types and missing values**, select a physical source column, and choose its declared type. Set units, explicit numeric culture (blank means invariant), UTC format or boolean tokens as applicable. Whitespace trimming and token case handling are opt-in. Set the missing-token count, then enter each exact token; an empty token field declares empty text missing. Use **Apply type** or **Clear type**. Data files are never rewritten by these actions.

Apply validates all current source corrections and dependent formula units before committing. Existing invalid raw fields remain visible. Type declarations and calculations share one atomic Undo payload (2 MiB UTF-8 maximum); Undo/Redo is validated before installation and refuses incompatible state without partial mutation. Current sheets/plots refresh, while previously frozen figures retain their own definitions. Workbook saving retains declarations. The explicit UTC plot/alignment key bindings still own their respective coordinate/key policies.

Validation: clean connected Unity compilation and 134/134 standard tests passed. The native authoring fixture covers rejected corrections, Apply/Undo/Redo, immutable draft ownership, conflicting units, Clear/Undo with matching formula/schema snapshots, retained row position, persistence and source byte preservation. Basic authoring is now integrated; broader admission, performance and platform acceptance remains open.

### Active source admission

`FPWorkbenchSourceDocument.CompileProgram(dataset)` compiles active formulas together with registered type declarations against the verified physical source. It returns a physical-only typed program when no calculated columns exist, or null for an entirely undeclared source. Presence-marker contradictions reject compilation. Workbook admission, same-content relinking, source opening, active table setup and alignment setup use this shared path. Structural validation now rejects malformed physical IDs and active type/formula unit conflicts early; actual source admission still checks actual column bounds and correction preconditions. Frozen figure/recipe definitions continue using their own snapshots.

Validation: clean connected compilation and 135/135 standard tests passed; a final presence-marker check passed in the 18/18 focused document suite. Coverage verifies explicit culture/units after save/load and relinking, physical-only sources, unit conflicts and unchanged original source/workbook state. This establishes consistent active-source interpretation, not scientific equivalence, complete key/UTC integration or target-platform performance.

### Shared table page accounting

`FPWorkbenchMemoryBudget(limitBytes, maximumWorkers)` is a caller-owned, thread-safe reservation ledger. `TryAcquire` refuses requests without creating a queue or starting work. A reservation's `Complete(retainedBytes)` releases worker/scratch capacity while retaining result accounting; `Dispose` releases it once. Cancellation must retain the reservation until its worker actually terminates. Oversized requests and invalid handoffs reject rather than exceeding the configured account.

Workbench Editor windows share a domain-scoped 256 MiB table-page account and at most two page workers. Admission estimates 8 MiB for a bounded page plus record/parser scratch (8 times the configured record bound plus column overhead). Publication accounts retained UTF-16 characters plus row/cell overhead; replacement/close releases it. Stale or cancelled tasks release only at termination. Sidebar counters show accounted reservations and page reads. A lifecycle guard prevents already-captured update callbacks from starting work after window close.

These estimates are not measured process heap, GC residency or GPU memory limits. Imports, plot/geometry payloads, direct runtime callers, clipboard/export buffers and disk-cache quota/LRU policies remain separate pending integrations. No latency or total-RAM qualification is claimed. Verification: clean connected compilation and 138/138 standard tests passed; 7/7 focused window tests also passed with post-run bytes/workers/leases observed at zero. Tests cover concurrency, refusal, retained handoff, invalid/double operations, cancelled pending ownership, page publication/resizing and late callbacks after close.

### Scalar query reservations

`FPWorkbenchFigureSession` accepts an optional caller-owned memory budget. Budgeted sessions require at least 2 MiB; each 512-bin viewport query reserves 1 MiB before dispatch, then shrinks to a retained-bin estimate on coherent publication. `WaitingForMemory`/`IsBusy` report deferred admission without spawning queued tasks. Replacing/clearing a view releases its account; cancelled/stale tasks remain accounted until terminal. `Reservation.ReleaseAfter(worker)` transfers cleanup without blocking and observes worker faults.

Editor registry-owned scalar sessions and Workbench table reads now share the same domain-scoped 256 MiB/two-read account. Embedded and detached views already share session ownership, so retaining another view does not duplicate result accounting. Final-owner close cancels workers and releases retained/pending reservations safely. Sidebar counters are named View reservations and Admitted reads to reflect the shared scope.

Clean connected compilation and 139/139 standard tests passed. Coverage checks capacity deferral, shared owners, replacement and terminal cleanup. After the full suite, bytes/workers/leases were all zero; the accounted peak was 17,826,560 bytes. These are ledger values, not measured Unity/CLR/GPU memory. Source-cache construction, overlays/spatial/histogram products, geometry/export/clipboard buffers and disk-cache quotas/LRU still need integration; no representative latency or platform performance claim is made.

### Overlay query accounting

Overlay Editor viewport queries now reserve 8 MiB in the same 256 MiB/two-read account used by tables and scalar sessions. A completed coherent response shrinks to retained-series/bin estimates. Capacity refusal reports Waiting for shared view memory/read capacity without launching a queued job. Rebuild/close releases retained data; stale/cancelled/faulted pending tasks release when terminal, and a late callback cannot restart reads after close.

Runtime overlay queries read series sequentially and publish only after all responses have matching source/range/revisions. This prevents one admitted overlay from launching eight simultaneous disk workers; it is not a claimed latency improvement. Typed physical series now use their actual cache/program fingerprints, including when no calculated axis is selected.

Clean connected compilation and 141/141 standard tests passed. Coverage adds typed decimal/missing overlays and native capacity deferral/resumption/close cleanup. Post-suite accounted bytes/workers/leases were zero. The pressure fixture intentionally reached the 256 MiB reservation limit; that value is not measured RAM. Spatial/geometry, histogram/export/clipboard, source-cache construction and disk quota/LRU integration remain pending.

### Spatial and geometry reservations

Scatter Editor queries reserve 16 MiB before sampling and shrink to retained point estimates. The 3D window now separates source-cache construction from query/preparation/upload: the latter reserves 64 MiB and publishes estimated retained raw/prepared point and readable mesh/GPU payload only after upload succeeds. Existing geometry remains accounted until replacement. Both paths defer allocation when shared capacity is unavailable, release stale/cancelled work on termination and block late callbacks after close.

Cancel point update applies to every 3D update stage, including waiting for memory, and preserves the previous batch/renderer. Whole-source construction is still outside interactive read admission. Geometry byte counts are estimates; render textures, driver allocation, GC residency and actual process/GPU usage are not established by the ledger. Scheduler priority/fairness, histogram/export/clipboard buffers and disk quota/LRU remain pending.

Verification: clean connected compilation and 142/142 standard tests passed. The final cancellation change passed the focused spatial-pressure fixture (1/1) and native 3D rendering/export fixture (1/1). Coverage checks exhausted-capacity deferral without point/mesh allocation, resumed publication, replacement/close, cancellation retaining old geometry and late callbacks. Post-test bytes/workers/leases were zero. No representative-data latency or platform-memory qualification is claimed.

### Histogram scans

Histogram fit and count passes now use shared byte reservations and a separate two-scan admission limit. Long histogram reads therefore do not occupy the two interactive page/plot admission slots. Each scan reserves 1 MiB plus parser/record estimates; successful histogram publication shrinks that account to retained bin/edge estimates. `Reservation.Reduce` supports reducing retained accounting after a separately admitted scan completes. These remain accounting models, not process-RAM measurements.

Capacity refusal leaves a pending request without queued tasks. Byte refusal rolls back its scan token. Cancel, Undo/Redo and close transfer active cleanup to terminal workers; cancelling a pending pass retains the previous result. Late callbacks cannot restart a disabled histogram window. Sidebar Background scans counters expose the separate admission class.

Clean connected compilation and 143/143 standard tests passed. Native coverage checks scan saturation with interactive admission still available, resumed fit/count, byte-pressure rollback, pending cancellation, result retention and close cleanup, plus existing histogram Undo/reopen/export behavior. Post-suite shared bytes/workers/leases and scan workers/leases were all zero. Other whole-source scans and image/clipboard/export buffers still need adoption; disk quotas and representative performance/platform qualification remain open.

### Owned disk-cache store

`FPWorkbenchDiskCacheStore` is a caller-owned runtime service for a dedicated cache root. It does not adopt or clean legacy caches. Open it with a byte quota, reserve an estimated write, publish only generated cache files into its unique destination, then commit an explicit relative-file list. A write reserves its estimate plus 1 MiB manifest overhead; commit rejects excess or undeclared files. The service cannot enforce an OS quota during arbitrary caller writes. Failed/uncommitted output stays unowned/protected and counts against subsequent inventory.

```csharp
using var store = await FPWorkbenchDiskCacheStore.OpenAsync(managedRoot, 4L * 1024 * 1024 * 1024, token);
using var writer = await store.ReserveAsync(maximumIndexBytes, token);
var data = await FPWorkbenchCsvDataset.ImportAsync(csvPath, writer.DirectoryPath, profile, token);
await writer.CommitAsync(new[] { "dataset.fpwm", "rows.fpwi" }, token);
if (!store.TryPin(writer.Id, out var reader)) throw new System.IO.IOException("Cache entry unavailable.");
using (reader) { var page = await data.ReadPageAsync(0, 16, 0, 2, token); }
await store.FlushAsync(token);
```

Keep a pin for every operation/view using an entry, and await operations/release leases before disposing the store. `TryPin` does no filesystem work and is not a substitute for dataset-content verification. `TrimAsync` removes least-recently-used unpinned entries only when their owned file set and hashes remain unchanged. Unexpected/modified files, unowned/corrupt directories and linked paths are protected/refused; protected/pinned content can prevent satisfaction of the quota. It deletes declared files individually and only their empty declared parent directories. Cancellation is observed between evicted entries. `FlushAsync` persists monotonic recent-use order, including after clock rollback.

Catalog bounds are 1,024 entries/16,384 files, with at most 1,024 declared files per entry and 240-character relative paths. These are application-accounting bounds, not measured process-memory guarantees. Seven store tests pass within the 158/158 standard suite, including quota/pins, ownership/path protection, restored LRU, cancellation and terminal commit cleanup. No new assembly dependency was needed.

Normal Workbench imports are not yet connected to this store. Editor configuration, active-view pins, derived-artifact ownership updates and index regeneration after eviction remain required before enabling automatic eviction for those paths. Existing qualification caches remain untouched.

### Early import preview

`FPWorkbenchCsvDataset.ImportAsync` accepts an optional final `IProgress<FPWorkbenchCsvPreview>` argument. Reporting occurs from import work before full-file hashing/indexing; use a thread-safe receiver or explicitly chosen dispatch context. The immutable prefix retains at most 32 rows/32 columns/512 Ki UTF-16 characters, preserves quoted/multiline values, and exposes text-limit status. It carries no verified dataset identity or total-row count. The full import still validates every record and source content before publishing its cache.

Workbench imports show the prefix as explicitly unverified/read-only until verification completes. The panel renders up to 12 rows/8 columns and labels shortened cell displays. Editing and plotting remain unavailable before admission. Preview delivery uses a closeable inbox; completion/failure/cancellation/close clear it, and late callbacks cannot restore closed state. Preview memory belongs to the active import reservation.

Clean compilation and 151/151 tests passed. Real Windows-1252 data also showed the prefix in the native Editor and then the verified 487,006-row/34-column table. These are functional results, not a representative performance or MATLAB-equivalence claim. Disk quota/LRU and the other finish criteria remain open; independent scientific references/tolerances are not yet available.

### Auxiliary reads and metadata operations

Selection-to-plot framing, alignment pair pages and aligned original-row lookup use shared interactive byte/worker admission. Pending requests create no task, keep frozen inputs, and preserve existing stale-result checks. Pair-page publication retains a reduced array reservation; replacements and close release it. Active cancelled work retains its reservation until termination.

Workbench save and correction-file import/export now acquire background/payload capacity before metadata serialization or source validation. A refusal starts no worker and creates no output; retry after capacity returns. Correction import accounts for the largest sequential source parser and retains its existing frozen-base conflict check. Replacing the workbook cancels correction operations owned by the prior context.

The standard suite passes 148/148 with pressure/recovery, retained viewport/page, pending-reveal close, correction-file source preservation and workbook-save saturation coverage. Reservation estimates are not total-memory or latency qualification; disk quotas, early preview, fairness/publication profiling and the other finish criteria remain open.

### Detached reload and recovery admission

Every detached figure family now admits serialized cache reopening through shared scan/payload limits. Verified parser profiles are serialized with windows; legacy windows reserve conservative supported bounds. Refused opens create no task, and window close transfers active ownership to terminal cleanup. Capacity waiting is shown separately from source verification.

Recovery metadata reads, replacement-source verification/index rebuilding and recovered-copy saves use the same admission. Refusal leaves the current recovery candidate/input intact and creates no output. Closed recovery windows ignore late update callbacks. The standard suite passes 148/148, including all five native serialized figure reloads under scan pressure and recovery read/save pressure, retry, preserved original bytes and final resource cleanup. Auxiliary readers and other outstanding resource/performance gates remain separate work.

### Import and workbook reopening admission

Workbench CSV imports, serialized sheet reloads and registered-source reopening reserve shared background/payload capacity before dispatch. Deferred imports create no cache. Pending operations remain cancellable, and closing/removing/replacing their owner transfers active reservation cleanup to terminal task completion. Verified import bounds are serialized with the window; older window metadata reserves the supported maximum parser until verification finishes. Linked-row navigation keeps its target while registered-source verification waits.

Whole-workbook verification reserves a conservative 168 MiB for sequential maximum-bound source verification and bounded metadata/definitions. It can wait for existing views to release capacity; the current workbook remains intact during refusal, cancellation or failure. This is reservation accounting rather than a measured process-memory guarantee. Saving waits for pending as well as active sheet verification. The runtime import/document services remain independently usable and caller-owned.

Clean compilation and 147/147 tests passed, covering no cache/worker on refused import, scan rollback, pending-close cancellation, successful retry/source preservation, workbook retention during pressure and saved-source verification cancellation/recovery. Detached source-opening/recovery/inspection paths and disk quota/LRU remain separate work.

### Alignment and aligned-regeneration admission

The Editor alignment window reserves shared background/payload capacity for source verification, mapping construction and matched-value materialization. Both source parser bounds are included, with 8 MiB verification or 32 MiB mapping/value allowances. Saved aligned-scatter regeneration uses validated saved import profiles to reserve before reopening/rebuilding original-source products. All underlying runtime APIs remain Editor-independent.

Deferred operations retain frozen request inputs and start no task/product while refused. Source/mapping/value admission rolls back correctly on payload refusal, and close/cancellation retains active reservations until task termination. Native alignment tests exercise saturation, frozen mapping options, value pressure, absent regeneration output while refused, pending-close cleanup, recovery and serialized restoration. The standard suite passes 146/146. These estimates are not measured total process-memory guarantees; general import/open/recovery and auxiliary readers remain separate integration work.

### Spatial and overlay construction admission

Editor XY scatter, XYZ and same-source overlay construction share background-job/payload admission, including cache verification. Their 8 MiB base allowance adds declared record/parser scratch and column overhead. Waiting requests retain frozen inputs rather than queuing tasks; failed byte admission releases its scan ticket. Completion and terminal cancellation/close release reservations. 3D cancellation also removes a deferred construction without replacing its previous renderer. Overlay construction remains sequential across its series.

The standard 146/146 tests pass with native exhausted-scan/exhausted-memory, rollback, recovery, replacement and close coverage. Source import, alignment and aligned-value regeneration remain outside this integration, and the account remains an estimate rather than measured total process RAM.

### Scalar construction admission

`FPWorkbenchFigureSession` accepts an optional `scanBudget` together with `memoryBudget`. One scan unit admits each series-cache construction/verification; payload accounting retains an 8 MiB allowance plus record/parser scratch and column overhead. The Editor figure registry supplies its shared accounts, so embedded and detached owners still share a single build. Direct runtime callers can supply their own accounts; existing calls remain compatible.

Refused construction remains pending without a queued worker. Memory refusal rolls back the scan ticket. Scan jobs retain bytes without holding interactive worker slots; close/cancellation releases only after the worker terminates. Embedded/detached UI distinguishes waiting from active cache work, and closed detached windows cannot reacquire leases through late update callbacks. Clean compilation and 146/146 tests passed; source import, other cache families and alignment remain separate admission work.

### Clipboard and CSV export accounting

Editor range copies and materialized CSV exports share the memory/background-job admission used by histogram and image jobs. Copy reserves a 12 MiB payload allowance and export 32 MiB, each plus declared record/parser scratch and column overhead. Capacity refusal leaves the existing clipboard/output unchanged and reports a retry message. Both selection and configured export ranges use the same admission path. These estimates do not measure total process RAM or OS clipboard storage.

Cancellation/window close retains ownership until the underlying task terminates; completed publication releases the account. Source bytes and frozen selection/correction semantics are unchanged. Connected compilation and 145/145 tests passed, including refusal/retry and deterministic held-worker close cleanup; shared bytes/workers/leases were zero afterward. Whole-source construction scans and disk quota/LRU remain outside this increment.

### Image capture and encoding accounting

Every Editor figure exporter now acquires shared memory/background-job admission before offscreen rendering, readback, GetPixels32 or worker snapshot cloning. The capture scope transfers its reservation to the runtime encoder/publication task; disposing the scope or closing/cancelling a window cannot release that account early. Pre-worker failures release immediately. Existing runtime single-image admission, no-overwrite publication and cooperative encoder cancellation remain in force.

The account model uses a 64 MiB PNG/provenance/overhead allowance, 40 bytes per output pixel and 128 bytes per estimated new geometry vertex. 3D exports reuse the already-accounted point mesh. This is not a measured total of Unity driver, font, native encoder, GC or process RAM. Image bounds alone do not guarantee available shared capacity: an export may require smaller dimensions/point budgets or closing unused figures. Requests are refused explicitly; resolution is never silently reduced. Sidebar Background jobs now includes histogram scans and image encoding/publication.

Clean connected compilation and 144/144 standard tests passed, including all five native export families. The focused image-job fixture passed 4/4; new checks cover capacity/oversize refusal, capture-scope disposal, worker ownership transfer, cooperative cancellation with work held, absent cancelled output and terminal cleanup. Post-suite shared/background counters were zero and ImageActive was false. Main-thread capture latency, representative RAM/GPU measurements, other source/export buffers and disk quota/LRU remain pending.
