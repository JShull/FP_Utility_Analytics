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
