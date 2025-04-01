using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace FuzzPhyte.Utility.Analytics
{
    /// <summary>
    /// This class works on order of operations tied to Unity
    /// Awake == Instance Setup
    /// Start == FP_StatReporter Setup
    /// Start (end of frame) == FP_Stat_Event Setup
    /// </summary>
    public class FP_StatManager: MonoBehaviour 
    {
        public static FP_StatManager Instance { get; private set; }
        [Tooltip("If we want to keep this around between scenes")]
        public bool KeepOnLoad;
        [Tooltip("Dictionary to hold all of our stats that are of type int")]
        protected Dictionary<FP_Stat_Type, FP_StatReporter> AlLStatsDict = new Dictionary<FP_Stat_Type, FP_StatReporter>();
        [Tooltip("List to store Events by Scene-all FP_Stat_Event register with this manager")]
        protected List<FP_Stat_Event> AllStatEvents = new List<FP_Stat_Event>();
        [Space]
        [Header("Events for Start/End")]
        [Tooltip("Called after we run our special start function")]
        public UnityEvent FinishedStartSpecialStat;
        [Tooltip("Called after we update/end all stat collection")]
        public UnityEvent FinishedAllStatEvent;

        /// <summary>
        /// Instance Setup
        /// </summary>
        public void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                Debug.LogWarning($"Awake via {this.gameObject.name}, I am the chosen one... I will live on forever");
                if (KeepOnLoad)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (Instance != this)
            {
                Debug.LogWarning($"My god I found my twin... I must now follow the codeway and end myself... this is the way");
                Destroy(gameObject);
            }
        }
        /// <summary>
        /// Should call this when we are setting up the stat
        /// </summary>
        /// <param name="name"></param>
        /// <param name="stat"></param>
        public void RegisterStatCollector(FP_Stat_Type name,  FP_StatReporter stat)
        {
            if (!AlLStatsDict.ContainsKey(name))
            {
                AlLStatsDict.Add(name, stat);
            }
        }
        
        /// <summary>
        /// Only use this if you need to remove a stat from the manager
        /// </summary>
        /// <param name="name"></param>
        public void RemoveStatCollector(FP_Stat_Type name)
        {
            if(AlLStatsDict.ContainsKey(name))
            {
                AlLStatsDict.Remove(name);
            }
        }
        
        /// <summary>
        /// We can use this to pass back our reference to the stat and talk directly to it as needed
        /// </summary>
        /// <param name="statName"></param>
        /// <returns></returns>
        public FP_StatReporter ReturnSpecificStatCollector(FP_Stat_Type statName)
        {
            if (AlLStatsDict.ContainsKey(statName))
            {
                return AlLStatsDict[statName];
            }
            else
            {
                return null;
            }
        }
        /// <summary>
        /// All Stat Events dial in and register with this manager
        /// should probably be a delegate :)
        /// </summary>
        /// <param name="theEvent"></param>
        /// <returns></returns>
        public bool RegisterAStatEvent(FP_Stat_Event theEvent)
        {
            if (!AllStatEvents.Contains(theEvent))
            {
                AllStatEvents.Add(theEvent);
                return true;
            }
            return false;
        }
        public bool RemoveAStatEvent(FP_Stat_Event theEvent)
        {
            if(AllStatEvents.Contains(theEvent))
            {
                AllStatEvents.Remove(theEvent);
                return true;
            }
            return false;
        }
        /// <summary>
        /// Public accessor to ask all registered Stat Events to end
        /// Should only call this once from a game manager on an invoked event
        /// </summary>
        public virtual void EndAllStatEvents()
        {
            for(int i=0; i<AllStatEvents.Count; i++)
            {
                var AstatEvent = AllStatEvents[i];
                if (AstatEvent is FP_Stat_GameClock)
                {
                    var gameClock = (FP_Stat_GameClock)AstatEvent;
                    gameClock.EndTimer();
                }
                else
                {
                    AstatEvent.EndStat();
                }
            }
            //call the Event to finish everything
            FinishedAllStatEvent.Invoke();
        }
        /// <summary>
        /// Starts stat events that need an initialization like the clock
        /// </summary>
        public virtual void StartSpecialStatEvents()
        {
            for(int i = 0; i < AllStatEvents.Count; i++)
            {
                var AstatEvent = AllStatEvents[i];
                if(AstatEvent is FP_Stat_GameClock)
                {
                    var gameClock = (FP_Stat_GameClock)AstatEvent;
                    gameClock.StartTimer();
                }
            }
            FinishedStartSpecialStat.Invoke();
        }
        /// <summary>
        /// Reset all Stats
        /// Only call this when we scene load and probably should be public yet
        /// </summary>
        public void StatReset()
        {
            AllStatEvents.Clear();
            AlLStatsDict.Clear();
        }
       
    }
}
