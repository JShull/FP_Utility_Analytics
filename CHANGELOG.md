# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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