using FuzzPhyte.Utility.Analytics;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FuzzPhyte.Utility.Analytics
{
    /// <summary>
    /// This is a "special" event as the game clock can be used for other things and also you can report it as a 'stat' but in most cases this is something that has a permanent home
    /// </summary>
    public class FP_Stat_GameClock : FP_Stat_Event
    {
        //[Space]
        //[Header("Game Clock Variables")]
        [HideInInspector]
        public bool StartTimerOnStart=false;
        private bool _paused = false;
        private bool _runningClock = false;
        private bool _timerFinished = false;

        public bool Paused { get => _paused; }
        public bool TimerFinished { get => _timerFinished; }
        public bool RunningClock { get => _runningClock; }
        
       

        private float _runningGamePauseTimeSeconds=0;
        public override void Start()
        {
            StartCoroutine(EndOFFrame());
        }
        public override IEnumerator EndOFFrame()
        {
            yield return new WaitForEndOfFrame();
            StatCollector = FP_StatManager.Instance.ReturnSpecificStatCollector(TheReporterDetails);
            if (StartTimerOnStart)
            {
                StartTimer();
            }
            FP_StatManager.Instance.RegisterAStatEvent(this);
        }
        /// <summary>
        /// If we need to reach back to the stat reporter to start the stat
        /// our events are normally not doing this as they are 'dumnb events' that just report data
        /// in some cases like our gameclock we might want to reach back from the event to kick things off vs the reporter doing the work
        /// </summary>
        public void StartClockReporter()
        {
            var statReporter = ReturnStatReporter() as FP_StatReporter_Float;
            statReporter.StartTheStat();
        }
        /// <summary>
        /// Is our stat reporter running?
        /// </summary>
        /// <returns></returns>
        public bool ReturnStatReporterRunning()
        {
            var statReporter = ReturnStatReporter() as FP_StatReporter_Float;
            return statReporter.TheStatData.ReturnStatRunning;
        }
        public void StartTimer()
        {
            NewStatEventReported();
            _runningClock = true;
        }
        /// <summary>
        /// This just starts a data collection to let us know how much time we are paused
        /// </summary>
        public void PauseTimer()
        {
            _paused = true;
            _runningClock = false;
        }
        /// <summary>
        /// this just ends a data collection period to let us know how much time we are paused
        /// </summary>
        public void UnPauseTimer()
        {
            _paused = false;
            _runningClock = true;
        }
        /// <summary>
        /// Public accessor to stop the game clock - this will also fire off the end of stat system
        /// </summary>
        public void EndTimer()
        {
            NewStatEventReported();
            if (StatCollector != null)
            {
                EndStat();
                _runningClock = false;
                _paused = false;
                _timerFinished = true;
                Debug.LogWarning($"Ended the stat collection for {this.gameObject.name}");
            }
        }
        /// <summary>
        /// Just using Unity to manage our time via Time.deltaTime
        /// we have an event the fires off at the beginning and at the end, total time as a calculator = total game time
        /// this is not a running time mechanism and we just subtract the time we were paused from the total time
        /// </summary>
        public void Update()
        {
            if (_paused)
            {
                _runningGamePauseTimeSeconds += Time.deltaTime;
            }
        }
        public float ReturnPausedTime()
        {
            return _runningGamePauseTimeSeconds;
        }
        
        public double AdjustDoubleTimeSecondsForPause(double passedTimeSeconds)
        {
            return passedTimeSeconds - _runningGamePauseTimeSeconds;
        }
    }
}
