// Copyright (c) 2026 John B. Shull
// FuzzPhyte LLC is a company associated with John B. Shull
// This file is part of FP_Utility_Analytics.
//
// Public license: GNU GPLv3-or-later.
// Commercial/proprietary use requires a separate license from John B. Shull.
//
// See LICENSE.md COMMERCIAL-LICENSE.md, and NOTICE.md.

using FuzzPhyte.Utility.Analytics;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FuzzPhyte.Utility
{
    [Serializable]
    [CreateAssetMenu(fileName = "StatDetails_", menuName = "FuzzPhyte/Utility/Analytics/StatDetails", order = 3)]
    public class FP_Stat_DisplayDetails : ScriptableObject
    {
        [Tooltip("The type for reference lookup later")]
        public StatCalculationType StatCalculationType;
        [Tooltip("Abbreviation for our Stat Display")]
        public string AbbreviatedStatDisplayName;
        [Header("Modify Stat Value Display")]
        public bool DisplayMathSymbol;
        [Tooltip("Display Math Symbol In Front")]
        public string SymbolMath;
        [Space]
        [Tooltip("If we want to display units")]
        public bool DisplayUnits;
        [Tooltip("Units we want to display")]
        public string StatUnits;


    }
}
