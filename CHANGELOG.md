# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Consolidate scalar binding, labels, axes, style, range and grid controls into one dockable Plot Settings panel following the focused sheet/Figure Window. Keep Figure Windows focused on the graph with compact navigation, point inspection and source-row reveal.

### Added

- Per-sheet cell grid toggle and conditional 0–1 opacity slider in Active sheet, using the existing table separator palette. Grid appearance saves/reopens with workbook sheet metadata and leaves data/plot caches unchanged.

- Embedded plot point inspection with a source-sample marker and Y-prioritized table navigation/axis-cell boxes; bounded cache reads respect source row spans and linear/log/inverted axes.
- Source unit presets, explicit custom override, and separate header-label/applied-unit displays. Header suggestions require Apply and do not convert values.
- Independently collapsible Active sheet and Row Range parameter sections, preserving window state across reloads.

- Saved per-sheet Row Range with inclusive table row numbers, table span reminder/boundaries/dimming, one-row boundary adjustments and reset. Sheet figures inherit source-row exclusion before statistics/summaries/sampling and update in place; detached panels keep docking and camera settings. Range changes support Undo/Redo and preserve raw data/source-row identities.

- Small C1/C2 and D1/D2 formula-reference labels in table headers, with full-name/unit tooltips and guidance beside calculated-column expressions. Labels follow source/definition order through hidden, frozen and reordered views.

- Dockable Workbook Settings panel opened from the parameter sidebar. Workbook actions, saved figures/sources, shared cache, correction files and CSV import are separated from active-sheet/plot controls; closing the panel preserves the owning workbook.

- Modular Data Workbench runtime engines and Editor tooling: background CSV indexes, streamed tables, early unverified previews and persisted workbook/sheet layouts.
- Nondestructive corrections/masks, declared types/units, limited calculated columns, Undo/Redo, correction files and selected-range CSV export.
- Scalar line/point, histogram, same-source overlay/dual-axis, aligned scatter and bounded 3D point viewers, with detached windows, presentation controls and PNG/provenance exports.
- Shared resource admission and lifecycle cleanup for import, table/plot queries, source recovery, metadata operations and exports.
- Owned disk-cache/quota engine with manifests and persisted LRU; managed imports and subsequent series/spatial/alignment/aligned-value products; independent view/worker leases; transactional workbook cache reconstruction with changed-original refusal.
- Compact local JSON cache settings, measured usage/reservation feedback, eligible-cache trimming and root/quota controls. Duplicate views verify/reuse the registered cache. Editor workflows now use managed admission and recovery; close/cancellation releases ownership, including late admission results.
- Annotated 3D image exports at 640×480 or larger: projected source-coordinate frame, axis labels/units and viewport limits, display/matching/budget counts and trajectory disclaimer, with matching provenance. Smaller legacy image sizes retain plain-cloud output.

- 3D Axis values toggle for source-coordinate tick labels and declared units, saved with figures and respected by annotated PNG exports; narrow large-offset ranges retain distinct numeric labels.
- Independent 3D origin/X/Y/Z bounds-frame visibility, a camera-following selected-point ring, and adjustable near clipping (default 0.001 normalized preview units). Visibility and near clip persist in figure metadata; annotated exports respect visibility.

### Fixed

- Workbench parameter sidebar now uses shared FuzzPhyte section dividers and headings; series bindings stay together, with separate figure-window, labels/axes, range, style, grid/limits and frame/fit groups.

- 3D axis segments now clip against camera depth and viewport boundaries instead of disappearing when an endpoint leaves the view.
- Workbench table/graph divider now preserves the grab offset and control capture across layout changes. Repeated vertical drags resize smoothly without reloading the table page.

### Documentation

- Completed the fixed Workbench delivery checklist: 170/170 connected Edit Mode tests, real-file isolated-cache eviction/recovery and regenerated figures, five inspected image/provenance exports, and recorded timings/memory limits in Documentation~/WorkbenchDeliveryResults.md.

- Consolidated the active delivery scope in Runtime/Workbench/DataWorkbenchPlan.md and shortened its README. Earlier progress/API notes are preserved under Documentation~/Archive; they are not additional delivery requirements.

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
