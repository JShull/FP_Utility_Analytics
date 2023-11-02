using System;
using UnityEngine;
using System.Collections.Generic;

namespace FuzzPhyte.Utility.Analytics
{
    public enum StatImmutable
    {
        StatInt,
        StatFloat,
        StatBool,
    }
    [Serializable]
    [CreateAssetMenu(fileName = "StatType_", menuName = "FuzzPhyte/Utility/Analytics/StatType", order = 0)]
    public class FP_Stat_Type : ScriptableObject
    {
        [Tooltip("Needs to be Unique")]
        public string StatIndex;
        public StatImmutable ImmutableStatType;
        [Tooltip("For UI and/or other needs")]
        public string StatDisplayName;
        
        [TextArea(3, 5)]
        public string StatDescription;
        [Tooltip("Stat details for displaying")]
        public List<FP_Stat_DisplayDetails> StatDetails = new List<FP_Stat_DisplayDetails>();
        [Tooltip("The FP Theme we are going to be referencing - this will help later for visuals")]
        public FP_Theme StatTheme;
    }
}
