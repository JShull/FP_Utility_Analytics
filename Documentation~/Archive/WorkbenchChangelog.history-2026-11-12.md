# Archived changelog snapshot

Historical detail retained before consolidating Workbench's Unreleased notes on 2026-11-12. Released history remains in the active CHANGELOG.md.

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Runtime owned disk-cache store with estimated quota admission, content ownership manifests, active leases, persisted monotonic LRU eviction and protected unknown/modified content; Workbench integration remains pending.

- Bounded immutable CSV prefix reporting before full hashing/indexing, with an explicitly unverified read-only Editor preview and close-safe worker delivery.

- Shared interactive admission for selection framing, alignment pages and aligned original-row lookup; background admission for workbook/correction metadata operations and cancellation on workbook replacement.

- Shared admission for all five detached figure source reloads and workbook recovery read/relink/save operations, with persisted parser bounds, pending requests and terminal cleanup.

- Shared admission for Workbench CSV imports, serialized sheet reloads, registered-source reopening and whole-workbook verification; pending cancellation and persisted parser-bound accounting.

- Shared scan/payload admission for alignment verification, mappings, matched-value materialization and saved aligned-scatter regeneration, with frozen deferred requests and terminal cleanup.

- Shared background admission for XY/XYZ and same-source overlay cache construction, frozen deferred requests, and terminal cancellation/close cleanup.

- Scalar series-construction scan/payload admission shared by embedded and detached figures, pending-capacity feedback, and detached late-callback lifecycle protection.

- Shared clipboard-copy and streamed CSV-export admission, explicit capacity refusal, and terminal-worker reservation ownership across cancellation/window close.

- Shared memory/background-job admission before figure image capture, transferred through encoder/publication lifetime, with explicit capacity refusal and unchanged requested output dimensions.

- Histogram fit/count byte reservations and separate background scan admission, preserving interactive read slots and supporting pending cancellation, Undo and terminal cleanup with retained-result accounting.

- Shared scatter result and 3D preparation/mesh reservation accounting with capacity deferral, terminal cleanup and cancellation across 3D update stages while retaining previous geometry.

- Overlay queries share the table/scalar resource account with bounded sequential per-series reads and terminal cleanup; typed physical overlays preserve and validate their program revision.

- Scalar query/retained-view memory reservations sharing the Editor table pool; capacity refusal defers current requests without queued workers, and shared figure owners retain one accounted view until final-owner cleanup.

- Shared Editor table-page byte/worker reservations, retained-page ownership and sidebar counters, with thread-safe caller-owned runtime accounting. Late update callbacks cannot restart reads after a Workbench window closes.

- Unified active-source program compilation across workbook admission, relinking and Editor consumers, with early active type/formula unit and identity validation while retaining independent frozen figure snapshots.

- Column type/missing-value authoring controls with culture, units, UTC and boolean policies; shared atomic calculation/schema Undo validates corrections and unit compatibility before Apply/Clear or Undo/Redo.

- Alignment Editor value programs now retain independent source type declarations for physical and calculated columns; saved aligned recipes regenerate numeric products from frozen parsing rules.

- Active Editor source declarations now supply typed programs to table/plot consumers even without calculated columns. Physical histograms retain typed program snapshots through saved-figure reopening; calculation authoring checks current declarations before commit.

- Typed physical scalar numeric X/Y interpretation and schema-qualified summary caches when a typed program is supplied, preserving explicit UTC binding and legacy untyped behavior.

- Optional immutable typed formula source snapshots with declared parsing/units and precision checks; schema-qualified physical scatter caches prevent reuse across different interpretations. Source-wide consumer integration remains in progress.

- Typed active-correction validation before Editor commit/import/Undo and workbook/relink admission, retaining explicit quality actions and rejecting incompatible overrides without coercion.

- Typed virtual table-page classifications with original/effective values, mask/missing quality preservation, schema-qualified stale-page checks and Editor state hints; typed edit/numeric consumers remain pending.

- Typed schema persistence and verified physical-column admission in registered workbook sources, retained through source refresh/relink and alignment metadata snapshots; typed data consumers remain pending.

- Runtime explicit physical-column type/null/culture/UTC declarations and immutable verified parsers, retaining exact integer IDs, raw text and masked parsed states; pipeline/Editor integration remains pending.

- Background PNG encoding/publication in every figure window, with shared job admission, cancel/status/timing UI and close/script-reload cancellation; main-thread capture remains explicit.

- Runtime bounded background RGBA8 PNG publication with exclusive admission, immutable request snapshots, cancellation/staging cleanup and encode/write metrics.

- Shared scalar/overlay PNG rendering with dual-axis scales/ticks/labels, color-keyed L/R legends and coherent completed-view provenance.

- Ordinary/aligned scatter PNG export with completed-view sampling/row provenance, both aligned original-source references and applied invalid-policy capture.

- Scalar figure PNG export preserving completed-view styles, gaps, envelopes and linear/log axes, with frozen source/settings provenance and stale-view refusal; shared bounded histogram/scalar image rendering.

- Explicit-size histogram PNG export with offscreen bars/axes/labels and published-result provenance, exact bins/counts, image hash and staged no-overwrite publication.

- Explicit-size 3D PNG export with a separate camera, bounded output dimensions, staged no-overwrite image/provenance publication and encoded-image hash; 2D/histogram PNG export remains pending.

- Bounded 3D point color/size appearance presets (`.fp3dplot`) and appearance-only Undo/Redo, retaining camera/source/query settings and existing renderer data.

- Frozen identifier columns in the virtual Editor table: header freeze/unfreeze, bounded pinned-width layout, projected paging, scrolling counts, range selection, linked reveal, saved stable-ID metadata and projection Undo.

- Bounded histogram presentation presets (`.fphistplot`) for density/count mode and grid visibility, with Undo/Redo and no source requery on application; shared staged preset file actions.

- Right-Y presentation preset actions for dual-axis overlays, preserving destination limits, shared-X and series controls, with atomic log-domain validation and presentation Undo/Redo.

- Double linear/Log10 axes for scalar, shared overlay/dual-Y and scatter views, with physical-unit ticks, logarithmic navigation, saved settings, presets and Undo. Nonpositive log domains report explicitly; mixed/nonpositive scalar summary bins are omitted with broken continuity and an inspection notice.

- Shared deterministic complete-date UTC parsing for scalar plots/alignment and early workbook binding validation, rejecting implicit current-date/timezone-dependent declarations.

- Histogram settings Undo/Redo with background query restoration and published-query persistence, preventing rejected drafts from replacing valid saved/reloaded figures.

- Frame selected table rows in the current scalar/shared figure using bounded cached endpoint reads, with preserved bindings/locked Y limits and stale-request protection.

- Exact cross-sheet composite UTC date/time alignment with explicit per-source formats, 100 ns integer keys, frozen date/time corrections and saved recipe/source-contributor support.

- Materialized CSV export directly from stable table selections, preserving selected nonadjacent column order and frozen source-row/formula/correction inputs.

- Stable-ID table range selection via click/Shift-click, workbook persistence and bounded background quoted-TSV copying with optional headers and Original/Effective/calculated values.

- Editor table column hide/restore and move-left/right actions with stable identity paging, per-sheet workbook/local persistence, layout Undo and linked-source reveal through hidden/reordered columns.

- Runtime stable-identity column projection and bounded noncontiguous virtual-table page reads, preserving original/corrected/calculated cell mappings for forthcoming table hide/reorder controls.

- Detached workbook source recovery for moved identical CSVs and missing indexes, with background identity verification/reindexing, preserved annotations/figures and separate recovered-copy saving.

- Reusable bounded `.fpplot` presentation presets for shared 2D figure settings, preserving target viewport/data bindings and using existing presentation Undo.

- Calculated-column X/Y selection for aligned scatter plots, using independently captured source formulas that persist in saved recipes.

- Workbook-owned aligned scatter recipes with original-source verification, frozen calculation/correction snapshots and background regeneration of missing derived caches while preserving the saved viewport.

- Cross-sheet matched XY scatter generation from the alignment window, with streamed physical/calculated value materialization, exact original-row links, frozen provenance, explicit invalid policy and short verified derived cache paths.

- Cross-sheet alignment Editor window with background source verification, explicit key/layer/domain policies, coverage counts, bounded pair inspection/reveal and applied-request reload restoration.

- Explicit shared-source-row and sorted exact-key alignment runtime with exact decimal/text policies, duplicate coverage, stable source-row pairs, frozen correction provenance and bounded staged cache products.

- Source-unit declaration authoring with dependent-formula validation, clear/Undo/persistence and table-header unit tooltips; strict 16-symbol unit-expression bound.

- Dual Y axes in shared-source multi-series figures, with independent limits/labels/grid/inversion, stable-series assignment Undo and workbook/window persistence without rebuilding data queries.

- Workbook-owned 3D point figures with stable XYZ identities, frozen snapshots, validated camera/clipping/appearance metadata and Saved figures reopen lifecycle.

- Bounded 3D XYZ point snapshot window with shared FuzzPhyte preview navigation, local-origin billboard rendering, source clipping, coverage/timing diagnostics and source-row inspection/reveal; runtime mesh/shader adapter with explicit ownership.

- Bounded local-origin 3D point preparation with double source frames, uniform scaling, preserved source rows and explicit float/reconstruction error reporting; no new package dependencies.

- Scatter-point reveal into the owning source table, including background closed-tab reopening, highlighted source rows/dependency cells and stable calculated-column dependency resolution without changing the current layer or plot viewport.

- Workbook-owned scatter figure descriptors and Saved figures integration with stable calculated axis IDs, frozen snapshots, validated budgets/colors/viewports and reopen support.

- Floating/dockable unsorted XY scatter snapshot viewer with calculated/source selectors, staged column drops, bounded point coverage, zoom/pan and source-row inspection.

- Unsorted double-precision 2D/3D point cache with source-row mappings, revision-qualified disk blocks, bounded deterministic spatial query output and explicit coverage/read counts; runtime foundation for scatter/3D viewers.

- Cancellable sequential materialized CSV export with explicit range/columns/layer/invalid policy, source/formula/correction provenance and staged no-overwrite publication; Editor sidebar export controls.

- Session-local figure presentation Undo/Redo across embedded/scalar/overlay controls, preserving viewport navigation and automatic bounds.

- Visible custom X/Y axis labels and collapsible detached figure controls for labels, styles and grid spacing, preserving viewport and saved settings.

- Persisted Line/Step/Points presentation for sorted-X figures, adjustable line/marker size and explicit coarse-envelope labeling.

- Calculated numeric X bindings across embedded and multi-series figures, revision-qualified caches and saved definitions; explicit rejection of invalid or decreasing coordinates while retaining duplicates.

- Mixed physical/calculated multi-series figures with frozen formula snapshots, stable-ID persistence and revision-checked calculated-header drop handling.

- Calculated-column histogram viewing with frozen formula/correction snapshots, stable-ID workbook persistence and verified source-schema admission.

- Calculated-header drag to embedded X/Y targets with stable-ID resolution and explicit rejection of foreign identities.

- Calculated Y plotting with stable definition bindings, revision-qualified caches, live formula/correction rebuilds and detached figure formula restoration.

- Calculated-column sidebar authoring and Undo/Redo, plus mixed source/calculated Editor table pages with distinct headers, correction-aware values and persisted virtual layouts.

- Source-owned calculation-definition persistence and a bounded virtual table adapter combining exact source values with revision-qualified calculated cells. Editor grid integration remains in progress.

- Persistent workbook source registry independent of open tabs, with closed-tab figure reopening, background source admission and indexed table reopening from the sidebar.

- Workbook-managed histogram/overlay figure descriptors and a saved-figures sidebar list, with stable identities, source/correction validation and reopen-after-close support.

- Multi-series snapshot viewer with up to eight shared-source Y columns, color legend, shared X navigation, union Y limits and column-drop append handling.

- Floating/dockable histogram viewer with background fit/build, explicit bins/range, count or density bars, coverage/exclusion feedback and reloadable source/correction snapshots.

- Bounded background histogram service for source/calculated columns with explicit bin edges, correction provenance, excluded-value counts and density normalization. Viewer integration remains pending.

- Staged source-column drag into embedded-plot X/Y targets, with identity validation and dedicated applied-binding Undo/Redo that preserves navigation and viewport settings.

- Runtime calculated-column arithmetic foundation with stable references, unit/cycle validation, correction-aware evaluation and explicit numeric error statuses. Editor integration and whole-range reductions remain in progress.

- Bounded non-destructive cell corrections/masks with original/effective views, dedicated Undo/Redo, correction-aware plot cache invalidation, workbook/reload persistence and standalone `.fpchanges` import/export.

- Versioned `.fpworkbook` document save/open with background source admission, atomic save, stable sheet identities and saved column/plot settings. Failed or cancelled opens retain the current workbook.
- Compact row/column navigation controls with table-edge hidden-column counts; backward column paging respects the widths of preceding columns.

- Initial Data Workbench runtime and Editor module: background bounded CSV record indexing, checksum-qualified cache reopening, 64-bit source cell/range identity, page reads, sheet tabs, captured splitters and row-domain numeric envelopes with configurable Y ticks/grid/inversion.
- Strict UTF-8 or explicit Windows-1252 import, quoted/multiline CSV handling, cancellation and no-overwrite cache publication. No new package dependencies.
- Workbench data and Editor interaction tests, plus opt-in qualification against the local recovered deployment CSV. Time-qualified TDR, persistent summary levels, full workbook documents, edits/formulas and cross-sheet bindings remain in progress.

## [0.4.0] - 2025-07-01

### 0.4.0 Added

- [@JShull](https://github.com/jshull)
  - FP Installer Support

## [0.3.0] - 2024-05-30

Bringing in Firebase support. This modifies some external requirements now tied to Google and this is currently done through a specific samples folder. This is initially for WebGL using an external repository created by GitHub user 'rotolonico' [Unity Firebase WebGL](https://github.com/rotolonico/FirebaseWebGL/releases) installed into the project. You will also need a configuration file (.json) in any Resources folder in the project.

### 0.3.0 Added

- [@JShull](https://github.com/jshull)
  - Firebase integration for WebGL
  - FP_FirebaseConfig.cs
    - Assumes a json file is in a Resources folder
  - FP_FireConfig.cs
    - serializable class for holding config information

## [0.2.0] - 2023-11-11

Fixing an issue with zero sized list in stat reporters and accounting for division by zero catches by adjusting the calculators to be a (double, bool) return type to screen against for when we waterfall through different calculations. The false return works it's way up the waterfall.

### 0.2.0 Added

- None

### 0.2.0 Changed

- [@JShull](https://github.com/jshull)
  - FP_Analytics.cs
    - All Calculators return types are now (double,bool)
    - Replaced exception throws with (0,false) returns
  - FP_Stat.cs
    - _statCalculations dictionary is now a (double,bool) to align with calculator returns
  - FP_StatReporter.cs
    - Matching previous updates from FP_Stat
    - All derived classes were updated to match as well
  - FP_StatManager.cs
    - Gave the user an option to destroy on load
  
### 0.2.0 Fixed

- This fixes the null issue within cases of 0 items being added to the stat reporter
- It will return a NaN value that you can screen against in your own use cases

### 0.2.0 Removed

- None...

## [0.1.0] - 2023-11-02

### 0.1.0 Added

- [@JShull](https://github.com/jshull).
  - Moved all test files to a Unity Package Distribution
  - Setup the ChangeLog.md
  - FP_Utility_Analytics Asmdef
    - FP_Analytics.cs
    - FP_Stat.cs
    - FP_Stat_Bool.cs
    - FP_Stat_Int.cs
    - FP_Stat_Float.cs
    - FP_Stat_Event.cs
    - FP_Stat_DisplayDetails.cs
    - FP_Stat_Type.cs
    - FP_StatManager.cs
    - FP_StatReporter.cs
    - FP_StatReporter_Float.cs
    - FP_StatReporter_Bool.cs
    - FP_StatReporter_Int.cs
  - All of these are considered utility analytic base classes. The FP_Analytics.cs contains the calculators. This package requires FP_Utility and is designed to be modified and extended with additional calculators and/or other 'base' classes, current calculators:
    - Sum, //SumCalculator
    - Average, //AverageCalculator
    - AverageTimeBetweenEvents, //TimeBetweenCalculator
    - MaxEvent, //MaxEventCalculator
    - MinEvent, //MinEventCalculator
    - StandardDeviation, //StandardDevCalculator
    - Variance, //VarianceCalculator
    - TotalTimeThere
  - Samples/SamplesURP
    - Install this!

### 0.1.0 Changed

- None... yet

### 0.1.0 Fixed

- Setup the contents to align with Unity naming conventions

### 0.1.0 Removed

- None... yet
