# FP Analytics Data Workbench

Status: proposed implementation plan, based on source review on 2026-11-11. No workbench implementation or performance qualification is claimed.

## Recommendation and existing code

Build a separate Data Workbench module inside FP_Utility_Analytics (display name FP Analytics). Keep existing gameplay/event statistics unchanged. Analytics owns general tables, sheets, plots and indexed data access. Biologging owns device import mapping, scientific processing, TDR presets, media synchronization and domain annotations. Analytics must not depend on Biologging.

Current findings:

- README.md describes small/medium event statistics. Runtime/Scripts/FP_Stat.cs stores List<StatReportArgs<T>> histories; that is unsuitable for an entire deployment. Do not route large tables through FP_StatManager.
- Editor/FP_GameClock_Editor.cs contains a commented-out clock inspector, not a reusable table/plot window. No package-local Research directory or tests were found.
- Package version is 0.4.0. package.json declares no dependencies although assembly definitions reference FP Utility. Audit package declarations during implementation without changing existing GUIDs.
- Biologging already has disk-backed timestamp lookup, bounded asynchronous chunk caching, cancellation/generation handling and Burst min/max jobs. Its TimelineReader currently reduces raw samples again per view; persisted summaries remain missing. Inspect and index probes still do synchronous I/O: do not copy that behavior into repaint paths.
- Reuse FP Utility menus/colors and established Biologging captured splitters, Undo and reload patterns. Extract focused generic behavior incrementally; do not copy the whole media window.

Host qualification starts on Unity 6000.6.7f1. Installed Burst 2.0.0, Collections 6.6.0, Mathematics 1.4.0 and Entities 6.6.0 are not automatically mandatory dependencies of the new module.

## Real-data evidence

The recovered raw deployment contains 39 CSVs, WAV/MAT audio, MOV video and a GPS workbook. Recovery CRC checks establish stored-byte integrity only. This review sampled one CSV header and initial rows, not the complete dataset.

That CSV has separate UTC/local date and time fields, numeric sensor channels, Depth (100bar) [m], empty cells, hexadecimal status and --- tokens. Initial rows are 0.0025 seconds apart; this does not establish each sensor's update rate. The sampled header is not valid UTF-8. Encoding, timestamp and missing-token mappings must be explicit. A depth label alone does not establish calibration or surface reference. The existing normalized numeric CSV importer cannot ingest this file directly.

## Package and platform boundary

Proposed additive folders/assemblies:

| Location | Responsibility |
|---|---|
| Runtime/Data | Runtime-compilable schema, source/page access, CSV/index/cache, summaries and workspace models; separate assembly with UnityEngine references allowed and no UnityEditor dependency |
| Editor/DataWorkbench | Reusable IMGUI table/plot views and public dockable EditorWindows; references Data and existing FP Utility editor APIs |
| Runtime/Acceleration, if justified | Optional Burst/Collections reduction adapter, with managed reference behavior |
| Tests/EditMode | Data/parser/cache/summary, workspace and interaction fixtures |
| FP_Biologging integration | Device profile and TDR preset; consumes Analytics contracts/views |

Keep existing runtime/editor assemblies and APIs intact. UnityEngine is an allowed runtime dependency; a pure .NET/noEngineReferences build is not a product requirement. The required boundary is runtime versus Editor: engines, algorithms, indexing, caching, calculations, workbook state and plot-data/axis computations must compile and operate without UnityEditor or Editor-only assemblies. Keep these services independently callable without opening an EditorWindow or requiring a scene/MonoBehaviour lifecycle merely to process data. They need not depend on the existing scene-stat assembly. Use composition and a small source/page contract with explicit ownership rather than a large provider framework. Biologging may reference Analytics; reverse references are forbidden.

Place EditorWindow/IMGUI interfaces, UnityEditor file dialogs, AssetDatabase actions, EditorApplication scheduling and Unity Undo integration in Editor-only assemblies. Runtime services expose explicit operations, cancellation/progress and reversible document changes; the Editor adapter connects those operations to Editor update/Undo APIs. Core behavior must not exist only behind UNITY_EDITOR conditionals. UnityEngine APIs used by workers must respect main-thread restrictions, and runtime file access must not assume Editor project paths.

No MATLAB runtime, Python runtime, database, plotting library, native plug-in or new UPM dependency is needed for the first slice. CSV/tabular text comes first. XLSX/MAT/Parquet readers, a general spreadsheet language and MATLAB execution are deferred. Non-destructive corrections and a limited derived-column expression system are planned below. EditorWindow is only the initial presentation adapter. A future Unity application, including an iPad application, can reuse the runtime engines with UnityEngine available while replacing the interface, lifecycle scheduling, file picking and storage integration. Standalone, iOS/IL2CPP, browser/threadless and restricted-filesystem targets require separate adapters and validation; runtime compilation alone does not establish device readiness.

## Data flow and responsiveness

Immutable sources -> bounded schema preview -> background indexing/conversion -> versioned disk caches and summary levels -> shared bounded page service -> visible table cells and plot geometry.

- Store raw data and caches outside Assets. Save workspace configuration separately; do not serialize source measurements into a Unity asset.
- Sequentially index/import in a worker with progress, cancellation, estimated disk use and atomic staged publication. Identify caches by schema/import settings/source identity and verification state. Size/mtime checks are not substitutes for content integrity verification.
- Open a bounded preview before full indexing finishes. Mark unindexed coverage explicitly; never display it as an empty recording.
- Preserve column identity/header/ordinal, units, typed values, missing/quality state and source file/row identity. Support integer, double, boolean, timestamp and text/status columns. Do not convert identifiers or large integers through double.
- Configure encoding, delimiter, quoted/escaped/multiline records, decimal culture, timestamp format/timezone and per-column missing tokens. Bound fields, records, column counts and retained text. Preview inference must be overridable, with malformed-row diagnostics.
- Use 64-bit row indices/offsets. Start with a record-offset index and selected typed-column caches. Share a timestamp vector where appropriate instead of repeating it for every channel. Estimate disk expansion before converting every column.
- Preserve source ordering, duplicates and independent sample rates. An unsorted table can be viewed, but time plots require an explicit sorted index or segmented time contract. Multi-file groups require compatible schemas and boundary/overlap/gap checks.
- No hashing, disk reads, parsing, waits or blocking job completion in OnGUI. Bound completed-result processing per update. Coalesce requests, cancel obsolete work, reject stale generations and prioritize visible data over prefetch.
- Share cache budgets across tables, plots and detached windows. Bound queues, strings, geometry, formatted cells and in-flight reads as well as resident numeric pages. Closing a view releases its leases; last-owner disposal cancels work and releases resources safely.
- Preserve previous plots with explicit range/revision/loading status during replacement. Distinguish pending, missing, invalid and failed table cells.

### Persisted summaries

Generate a multi-resolution summary pyramid during import. Each time bucket retains min/max with source locations, first/last samples, counts, quality and continuity/gap information. Choose a level based on visible pixel width; read raw detail only when needed. Merging must not hide spikes or join gaps.

Exact table/cursor values always resolve to source records, never an averaged display bin. Scatter needs its own bounded density/selection policy; arbitrary XY geometry cannot use a time envelope unchanged. Rendering and axis changes never alter measurements.

## Workspace, sheets and tables

Use the existing FuzzPhyte multi-pane IMGUI style: resizable/collapsible source sidebar, sheet tabs, adjustable table/figure split and progress/status footer. Each grip owns only its adjacent section.

Sheets are named views over registered datasets; they do not copy the dataset. Support rename/reorder/close/reopen and explicit assignment of files or compatible file groups. Different-rate streams remain separate unless the user chooses an alignment policy. Closing a sheet never deletes files.

Virtualize rows AND columns with small overscan. Provide resizable/reorderable/hidden columns, frozen header, optional frozen identifier columns, row numbers, cell/range selection, go-to-row/time and bounded clipboard export. A logical paging/scroll model must handle long row indices; do not construct a float-height rectangle for billions of rows.

The first table slice is read-only, but its identity/page contracts must support the correction and derived-column stages without replacing the storage model. Full-dataset sorting/filtering later needs cancellable background external/index-based execution and disk limits. Never label a page-only sort as a full-table sort. A general spreadsheet language remains outside the initial scope.

Link table and plot selection through source/row/column identity and optional time bounds. Linking navigation must not silently seek a Biologging media playhead; selection, viewport and playback cursors are separate states.

## Workbook, cell and range identity

A workbook is the saved analysis document containing source registrations, sheet definitions, corrections, derived-column definitions and figures. It references source data rather than embedding a full dataset. Multiple sheets can share a dataset and cache while retaining independent views. A closed sheet remains available to existing bindings until explicitly deleted; deleting a referenced definition must show its dependents and leave an explicit unresolved reference rather than silently retargeting it.

Define these contracts in A0, before the table UI:

- Stable WorkbookId, SheetId, DatasetId, ColumnId and source-revision identity. Display names and column letters are labels, not persistent keys.
- A source row key includes the source file/partition identity and a 64-bit logical record ordinal in its verified revision. A timestamp alone is not a row key: duplicates are possible. Quoted multiline CSV records count as one record; byte offsets are lookup data, not durable identity.
- A cell reference resolves sheet context, dataset/source revision, row key and ColumnId. Derived columns have their own stable IDs and resolve against explicit input bindings. Source corrections are dataset-scoped and shared by sheets referencing that revision; sheet-specific alternative processing uses an explicit derived dataset/branch, not an invisible override.
- A view-row index is separate from the underlying row key. Sorting, filtering, column reordering and sheet renaming must preserve the target of existing edits, formulas, selections and plots. Changing a sheet's source cannot silently rebind old references.
- Cell/range lookup supports single cells, rectangular ranges, disjoint selections and whole-column bindings without allocating every selected cell. Define internal ranges as zero-based, half-open intervals; display row numbers may be one-based. Unknown row counts remain explicit until indexing completes.
- A1-style addresses such as Depth!B12 and Depth!B12:B100 may be accepted as navigation/input conveniences. Resolve them to stable bindings when saved; retain the display text for users. Relative-copy behavior, inserted rows and automatic spreadsheet reference rewriting are deferred, not implied by A1 syntax.
- Centralize checked bounds, type/null/error reporting and asynchronous page resolution in the data service. Do not let individual plots invent their own row-to-source addressing. Missing/stale/deleted references are visible errors with a relink action.

## Non-destructive editing and derived data

Keep three layers separate: immutable source measurements; a versioned correction/quality overlay; and derived columns or datasets. Effective values are resolved from those layers using an explicit revision. No normal edit/save/export operation overwrites the raw source.

### Corrections and difference files

Store sparse edits in a sidecar changes file associated with the workbook and verified source revision. Each operation records stable cell/row/column identity, original typed value or precondition, replacement/quality mask, operation ID/order, reason and recorded edit time. Optional author attribution must be explicit, not guessed. Distinguish null, invalid, masked and deleted states; a bad-sample mask does not erase the recorded value.

Initially support typed cell overrides, range/batch corrections and quality masks. Inserting/deleting source rows and rewriting timestamps are deferred structural operations requiring new row/clock/index contracts. Reject values that violate column types; changes to units or time interpretation are explicit metadata/processing revisions.

Provide Undo/Redo with one action per completed edit or batch, original-versus-effective inspection and a clear modified-cell indicator. Removing a correction restores the source value. Reject or quarantine overlays whose source hash/revision or cell precondition no longer matches; never apply them by current screen position.

Use versioned JSON for small metadata/overlays, with bounded indexed sidecar pages or compact range operations for large changes. Do not serialize millions of corrections into an ever-growing in-memory JSON object. Use atomic saves and preserve explicit unsaved/failed status. A workbook save records document/overlay references consistently; reload must not expose a partially committed revision.

### Added and calculated columns

Support user-added typed annotation columns as sparse sidecar values and calculated columns as versioned expressions with stable input references. For example, Total = ColumnA + ColumnB calculates a value per aligned row; SUM(ColumnA) is a dataset/range reduction and is a different operation. Do not interpret a plot's downsampled bins or visible table page as the whole-column input to a sum.

Start with constants, typed column references, arithmetic and explicit SUM/COUNT/MIN/MAX/MEAN reductions over declared ranges. Specify missing/nonfinite behavior, integer overflow, division by zero, numeric precision and unit compatibility. Do not silently add incompatible units or convert timestamps/identifiers to floating point. A limited expression parser is native C#; arbitrary C# evaluation, macros and third-party formula engines are excluded.

Maintain a dependency graph, reject cycles and show unresolved references. Compute row-wise formulas on requested chunks; full-range reductions run in cancellable background passes with progress. Cache derived pages and plot summaries by source, correction, formula and alignment revisions. An edit invalidates affected dependency pages and summary ancestors; global reductions invalidate their consumers. Undo/Redo creates coherent revisions and must never publish mixed old/new plot results.

### Save, compare and export

Save the workbook, change set and calculation definitions without copying original recordings. Allow original/effective layer selection consistently in tables, formulas and figures; show which revision/layer is displayed. Export a new materialized CSV containing explicitly selected columns, corrections and derived results, or export the portable change set separately. Include provenance, calculation/alignment settings and source identities in accompanying metadata. Export is streamed, cancellable and staged; source files are never the implicit output path. These changes add editing capabilities in stages; they do not promise a full Excel/MATLAB execution environment.

## Generate plots from sheet columns

Provide Create Plot from selected columns/ranges and an editable series-binding panel. Each series stores its X/Y column or expression references, sheet/dataset IDs, selected row/range policy, original/effective layer and alignment settings. Allow multiple Y columns from one sheet and multiple series from different sheets in one figure. Save bindings by identity, not header text or temporary arrays.

For a single sheet, default X/Y pairing uses the same source row keys. Users can choose a time column or an explicitly labeled row-index axis. Selected ranges remain fixed selections; whole-column bindings cover the dataset with visible indexing/coverage status, not only the loaded page.

Cross-sheet plotting has two distinct operations:

1. Overlay independent series, each with its own X and Y from its own sheet. No row join or resampling is necessary. Validate axis units/time origins, and convert only under a declared rule; show incompatible series separately or request a mapping.
2. Pair X from one sheet with Y from another, or calculate across sheets. Require an explicit alignment: verified shared row identity, exact key/time match, nearest time within a tolerance, or declared interpolation/resampling. Positional pairing is an opt-in with equal-length and ordering checks, never the default. Define duplicate-key ties, unmatched rows, clock transforms, gap limits and interpolation validity. Record the policy and parameters as provenance. Implement exact/shared-row alignment first; advanced policies follow with dedicated tests.

Use bounded indexed joins; unsorted keys may require a background external index. Show matched/unmatched/ambiguous counts and available coverage. Retain input row identities for inspecting derived/aligned points. Plot clicks resolve original contributing cells even across sheets; table selection can highlight all relevant series without changing media playback.

Changing edits, formulas, source revisions or alignment settings regenerates affected derived pages and summaries before replacing the displayed plot. Disconnected windows share the same figure/document and revision model. The existing configurable axes, grids and export controls apply equally to single-sheet and cross-sheet figures.

## Configurable plots and windows

Target everyday 2D scientific plotting, with explicit scope rather than MATLAB/toolbox parity:

- Assign X/Y columns; multiple named series; line, step and scatter views; visibility, color, line width, markers and legend.
- Title and axis labels/units; linear/log scales, inverted axes, manual/automatic limits and independent lock/fit. Define nonpositive-log handling.
- Automatic nice ticks or manual major spacing/origin, minor subdivisions, grid visibility/color/opacity and numeric/scientific/elapsed/UTC formatting. Grid changes do not change limits or source samples. Bound tick counts when spacing is tiny.
- Pointer zoom, captured pan, rectangular selection, reset/fit, cursor inspection, linked X axes and stacked/subplot layouts. Locked Y limits remain stable while panning.
- Save presets; export PNG with explicit dimensions and bounded selected-data CSV. Later: vector export, dual Y axes, histograms, error bars and richer annotations.

Workspace figure tabs can open as ordinary public Unity EditorWindows, float or dock through Unity's window controls, and reattach to the workspace. Do not use internal docking APIs or invent an OS window manager. Shared FigureId/document state prevents duplicate caches; explicit Duplicate creates independent settings.

Persist versioned JSON workspace/source references/import profiles/sheets/figures/links. Keep open handles, tasks and data buffers transient. Restore/relink missing sources after reload, with caches revalidated. Use Undo for document/settings changes without reverting unrelated playhead/layout state. Existing Biologging FPBS/FPTI and .fpreview compatibility must remain intact.

## Biologging TDR integration

Biologging provides a preset with explicit UTC/session time on X, declared depth in metres on Y and positive depth downward through axis inversion. Include full-deployment overview, gaps/quality, source row identity and linked detail/table inspection.

Confirm encoding, UTC origin, depth calibration/sign/surface reference and file continuity with the collaborator. Pressure-to-depth conversion, surface correction, filtering and dive detection are explicit versioned processing stages, not hidden plot defaults. Plotting raw depth does not establish scientific equivalence to a MATLAB processing pipeline.

## Delivery plan

| Stage | Deliverable | Acceptance gate |
|---|---|---|
| A0 | Source/schema/import/workbook contracts, stable cell/range identity, overlay/formula/series revision contracts, real CSV inventory and benchmark harness | Confirm encoding/time/null policies and file boundaries; distinguish source keys from view positions; record baseline hardware and throughput |
| A1 | Background CSV index/page service, bounded typed cache, virtualized table and sheet tabs | Exact values/row identity; malformed/quoted/multiline tests; 64-bit navigation; cancellation/reload/leak tests |
| A2 | Persisted summaries, plots from selected columns/ranges, axes/ticks/grids and linked inspection; independent multi-sheet overlays and shared-row/exact-key bindings | Spikes/gaps/duplicates preserved; summary-vs-raw tests; correct contributing cells; explicit mismatch/duplicate policy; transforms/ticks; stale requests; no source I/O during repaint |
| A3 | Resizable/float/dock workspace, shared ownership, Undo, presets, relink and exports | Multiple windows share bounded cache; safe teardown/reload; independent grips; export limits |
| B1 | Biologging device profile, embedded views and TDR preset | Compare source rows/time-depth pairs with independently supplied MATLAB export; agree tolerances first; existing reviews remain compatible |
| A4 | Non-destructive typed edits/masks/annotation columns, difference files, Undo/Redo and original/effective comparison | Source bytes unchanged; stable targets after sort/reorder; source mismatch detection; atomic overlay save/reload; coherent plot invalidation |
| A5 | Limited calculated columns/range reductions, dependency tracking, derived summaries and materialized export | Arithmetic/null/unit/precision tests; cycle detection; chunked versus reference results; full-range reduction semantics; cancellation and bounded caches |
| A6 | Global sort/filter, advanced cross-sheet alignment, broader plots and platform adapters | Explicit whole-dataset semantics, bounded joins/external operations, contributing-row traceability and target-specific tests |

Start with A0 and an A1/A2 vertical slice on one raw CSV using time/depth only. Expand to all 39 CSVs after timestamp/file-boundary policies are qualified. Avoid a wholesale rewrite of the established media timeline.

## Proposed performance and validation contract

These are unmeasured desktop-SSD targets, not guarantees:

- Default 128 MiB shared workbench-owned cache/queued payload allowance, including numeric/text/geometry buffers. Report Unity baseline, allocator/task overhead, OS caching and media separately; measure total process-memory delta too.
- Main-thread result processing <=2 ms/update; UI handler/repaint p95 <=8 ms on reference hardware. Cache-hit response <=100 ms; indexed cold navigation <=500 ms. Record p50/p95/p99 and cache state.
- Whole-deployment overview reads width-appropriate summaries, not all original rows per pan/zoom. Geometry/tick count scales with viewport. Import is background/cancellable, with early preview and measured throughput.
- Benchmark actual CSV bytes separately from the approximately 18.7 GB raw archive's media. Add reproducible larger numeric/mixed synthetic fixtures; do not commit private deployment data.
- Managed workers handle parsing and files. Use optional Burst for measured numeric reductions; keep a managed baseline. Entities is not required for a table or plot and should not represent one entity per row.

Implementation must compile and run Unity Test Framework tests through the connected host. Include worker errors, corrupt/truncated caches, cancellation/disk-full publication, timestamp resets/day rollovers, large integer/time precision, multiple-file overlap, multi-window ownership and domain reload while reads are pending. Verify runtime assembly dependency closure contains no UnityEditor/Editor-only references, and exercise import, page reads, calculations and plot-data generation without an EditorWindow. Add a Player compilation/smoke-test gate for the runtime assemblies; validate iOS/IL2CPP, file permissions/storage, lifecycle and device performance separately before claiming iPad support. Scientific comparison requires independent reference outputs and collaborator-approved bounds.

Add identity/edit/formula tests for renamed sheets, reordered columns, sorted/filtered view rows, duplicate timestamps, multiline records, multiple files and row indices beyond Int32. Verify sparse edit replay, source fingerprint/precondition conflicts, quality masks, Undo/Redo, unsaved reload and original-file hashes before/after. Cross-sheet fixtures must cover mismatched lengths, exact/nearest ties, unequal rates, timezone/clock differences, gaps, missing keys and invalid interpolation. Verify whole-column sums exceed the visible page correctly, dependency cycles fail explicitly, and obsolete formula/summary generations never overwrite newer edits. All multi-window readers and derived caches remain within the shared budget.

This planning change was statically reviewed; no Unity compile/test or performance run was needed or performed.

## References

Repository: README.md; Runtime/Scripts/FP_Stat.cs; Runtime/Scripts/FP_Analytics.cs; package.json and asmdefs; ../FP_Biologging/Research/EngineeringFoundation.md; ../FP_Biologging/Runtime/Unity/FPBiologgingTimelineReader.cs; ../FP_Biologging/Runtime/FPBiologgingCsvImporter.cs. Biologging research is relevant context, not a generic analytics specification.

MathWorks workflow references (not implementation dependencies):

- https://www.mathworks.com/help/matlab/tall-arrays.html
- https://www.mathworks.com/help/matlab/import_export/tall-data-visualization.html
- https://www.mathworks.com/help/matlab/ref/tiledlayout.html
