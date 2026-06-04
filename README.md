# FP Utility Analytics

FP Utility Analytics is a lightweight stat collection and calculation package for
Unity projects. It is designed for simple gameplay, UI, and event analytics where
you want local stat histories, summary calculations, and ScriptableObject metadata
without bringing in a full database or external analytics service.

Use this package for small to medium in-scene stats such as score events, pickups,
timers, booleans, averages, min/max values, and game-clock timing. For high-volume
telemetry or production-scale data storage, use a dedicated analytics backend.

## What It Provides

- `StatReportArgs<T>`: a timestamped stat entry with details and typed data.
- `FP_Stat<T, A>`: a non-Mono stat history with start/end/reset and calculator support.
- `FP_StatReporter`: a MonoBehaviour bridge that registers a stat collector with `FP_StatManager`.
- `FP_StatReporter_Int`, `FP_StatReporter_Float`, and `FP_StatReporter_Bool`: typed reporters for common stat values.
- `FP_Stat_Event`: a scene event component that sends values into the matching reporter.
- `FP_Stat_GameClock`: a special stat event for game-clock timing with pause support.
- `FP_StatManager`: a singleton registry for stat reporters and stat events.
- `FP_Stat_Type`: ScriptableObject metadata for stat identity, type, display name, details, and theme.
- `FP_Stat_DisplayDetails`: ScriptableObject metadata for displaying calculation values.
- `FP_WeightedPicker`: utility methods for weighted random selection.
- Samples for local stat usage and optional Firebase configuration loading.

## Requirements

- Unity `6000.0.1f8` or newer, based on `package.json`.
- `com.fuzzphyte.utility`.
- TextMeshPro for sample UI scenes.
- Optional Firebase SDK only if you import and use the Firebase sample.

See [Installation~/Dependencies.md](Installation~/Dependencies.md) for the FP Utility dependency note.

## Runtime Architecture

### Stat Data

`FP_Stat<T, A>` stores the history for one stat.

- `T` is the raw data type stored by events.
- `A` is the converted display/output type.
- `StatStart()` opens collection.
- `StatNewEntry(...)` records entries only while the stat is running.
- `StatEnd()` closes collection and, in typed subclasses, runs calculators.
- `ReturnCalculatorResults(...)` returns `(double result, bool validCalculation)`.

Typed implementations include:

- `FP_Stat_Int`
- `FP_Stat_Float`
- `FP_Stat_Bool`

### Stat Reporters

Reporters are the MonoBehaviour side of the stat system.

- Add a typed reporter component to a GameObject.
- Assign an `FP_Stat_Type`.
- Choose `CalculationTypes`.
- Enable `StartOnStart` or start the stat manually.
- Reporter registration happens through `FP_StatManager`.
- `EndStatData()` ends the stat and invokes reporter listeners.

Typed reporters expose `NewStatData(...)` helpers:

```csharp
bool stored = false;
var entry = scoreReporter.NewStatData("Score Added", ref stored, 25);
```

### Stat Events

`FP_Stat_Event` is a scene component that reports into the collector associated
with `TheReporterDetails`.

- It waits until end of frame, finds the reporter from `FP_StatManager`, and registers itself.
- `NewStatEventReported()` reports default values based on the stat type.
- Overloads accept `int`, `float`, `bool`, and `FPFloatEvent`.
- `EndStat()` ends the connected reporter and logs calculation results.

The stat type controls which reporter cast is used:

- `StatImmutable.StatInt` -> `FP_StatReporter_Int`
- `StatImmutable.StatFloat` -> `FP_StatReporter_Float`
- `StatImmutable.StatBool` -> `FP_StatReporter_Bool`

### Stat Manager

`FP_StatManager` is the scene registry.

- Keeps one singleton instance.
- Can optionally persist with `KeepOnLoad`.
- Registers `FP_StatReporter` instances by `FP_Stat_Type`.
- Registers `FP_Stat_Event` instances.
- `StartSpecialStatEvents()` starts special events such as `FP_Stat_GameClock`.
- `EndAllStatEvents()` ends every registered stat event.
- `StatReset()` clears registered reporters and events.

If a reporter starts and no manager exists, the reporter creates a `StatManager`
GameObject and registers itself.

## Calculations

Supported `StatCalculationType` values:

- `Sum`
- `Average`
- `AverageTimeBetweenEvents`
- `MaxEvent`
- `MinEvent`
- `StandardDeviation`
- `Variance`
- `TotalTime`

Calculators return `(double, bool)` so empty histories, invalid conversion functions,
and other non-calculable states can be handled without exceptions in the normal flow.

Formatting helpers live in `StatCalculationFormat`:

- `ReturnFormatStatType(...)`
- `ReturnStandardTimeFormat(...)`
- `ReturnStandardFormatDouble(...)`

## Quick Start

Create a stat type asset:

1. In Unity, create `FuzzPhyte/Utility/Analytics/StatType`.
2. Set `ImmutableStatType` to `StatInt`, `StatFloat`, or `StatBool`.
3. Fill in display name, description, and optional display details/theme.

Set up a reporter:

1. Add `FP_StatManager` to the scene, or let the first reporter create one.
2. Add a matching reporter to a GameObject, such as `FP_StatReporter_Int`.
3. Assign the same `FP_Stat_Type` asset.
4. Add the calculations you want to `CalculationTypes`.
5. Enable `StartOnStart`, or call the reporter's start method when your game begins.

Report stat data from code:

```csharp
using FuzzPhyte.Utility.Analytics;
using UnityEngine;

public class ScoreExample : MonoBehaviour
{
    public FP_StatReporter_Int ScoreReporter;

    public void AddScore(int amount)
    {
        bool stored = false;
        ScoreReporter.NewStatData("Score Added", ref stored, amount);

        if (!stored)
        {
            Debug.LogWarning("Score stat was not running.");
        }
    }

    public void FinishScoreStat()
    {
        ScoreReporter.EndStatData();
        var total = ScoreReporter.ReturnStatCalculation(StatCalculationType.Sum);
        Debug.Log($"Total score: {total.Item1}, valid: {total.Item2}");
    }
}
```

Or report through `FP_Stat_Event`:

```csharp
public class PickupExample : MonoBehaviour
{
    public FP_Stat_Event PickupEvent;

    public void OnPickupCollected()
    {
        PickupEvent.NewStatEventReported(1);
    }
}
```

## Game Clock

`FP_Stat_GameClock` is a special float stat event for timing.

Common flow:

```csharp
GameClock.StartClockReporter();
GameClock.StartTimer();
GameClock.PauseTimer();
GameClock.UnPauseTimer();
GameClock.EndTimer();
```

Useful accessors:

- `RunningClock`
- `Paused`
- `TimerFinished`
- `ReturnPausedTime()`
- `AdjustDoubleTimeSecondsForPause()`

The game clock records a start event and an end event. `TotalTime` can then be used
to calculate the stat duration, while `AdjustDoubleTimeSecondsForPause()` gives a
Unity realtime value adjusted for pause time while the timer is running.

## Weighted Picker

`FP_WeightedPicker` provides two helper methods:

- `PickFromCounts(int[] counts, float smoothK = 0f, float temperature = 1f)`
- `PickFromProbabilities(float[] probs, float temperature = 1f)`

Both return a selected index, or `-1` when no bin is pickable. Zero and negative
weights are treated as unpickable.

## Samples

Import samples through Unity Package Manager.

### Simple Event Stats

`Samples~/SamplesURP` includes:

- Example `FP_Stat_Type` assets.
- A sample analytics scene.
- UI/theme assets used by the stat display examples.

### Google Firebase Integration

`Samples~/SamplesFirebase` includes:

- `FP_FireConfig`: serializable Firebase config data.
- `FP_FirebaseManager`: sample loader that reads a config `TextAsset` from `Resources`.
- A sample scene and ScriptableObject assets.

Firebase is sample-specific. Install and configure the Firebase Unity SDK separately
before using that sample in a project.

## Extending

To add a new stat value type:

- Create a new `FP_Stat<T, A>` subclass.
- Provide a conversion function from `T` to `double` for calculators.
- Override `StatConversion(...)` for display conversion.
- Run calculators when the stat ends.
- Create a matching `FP_StatReporter` subclass for the MonoBehaviour side.
- Add event reporting paths that cast to your reporter type.

To add a new calculation:

- Add a value to `StatCalculationType`.
- Create an `AStatCalculator<T>` implementation.
- Add the calculation branch in `FP_Stat<T, A>.RunCalculators(...)`.
- Add a display label in `StatCalculationFormat.ReturnFormatStatType(...)`.

## Dependencies

Please see [package.json](package.json) for package metadata.

Runtime assembly reference:

- `com.fuzzphyte.utility`

Sample/editor assemblies additionally reference analytics, utility samples, and Unity editor APIs where needed.

## License Notes

See [LICENSE.md](LICENSE.md) for details.

## Contact

- [John Shull](mailto:JShull@fuzzphyte.com)
