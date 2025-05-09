namespace FuzzPhyte.Utility.Analytics.Editor
{
    using UnityEngine;
    using UnityEditor;
    using System;
    /*
    /// <summary>
    /// Editor class to help with the clock
    /// </summary>
    [CustomEditor(typeof(FP_Stat_GameClock))]
    public class FP_GameClock_Editor : UnityEditor.Editor
    {
        private FP_Stat_GameClock _gameClock;
        SerializedProperty SOReporterDetails;
        SerializedProperty SOStatCollector;
        //SerializedProperty EventDetails;
        private double _lastRepaintTime;
        

        private void OnEnable()
        {
            _gameClock = (FP_Stat_GameClock)target;
            //_gameClock.TheReporterDetails = (FP_Stat_Type)EditorGUILayout.ObjectField("TheReporterDetails", _gameClock.TheReporterDetails, typeof(FP_Stat_Type), false);
            //_gameClock.StatCollector = (FP_StatReporter)EditorGUILayout.ObjectField("StatCollector", _gameClock.StatCollector, typeof(FP_StatReporter), true);
            //_gameClock.StatCollector = (FP_Stat_Type)EditorGUILayout.ObjectField("Stat Collector", _gameClock.StatCollector, typeof(FP_Stat_Type), false);
            //_gameClock.TheReporterDetails = (FP_StatReporter)EditorGUILayout.ObjectField("Reporter Details", _gameClock.TheReporterDetails, typeof(FP_StatReporter), true);
            //SOReporterDetails = serializedObject.FindProperty("TheReporterDetails");
            //SOStatCollector = serializedObject.FindProperty("StatCollector");
            //EventDetails = serializedObject.FindProperty("EventDetails");
        }
        private void OnEditorUpdate()
        {
            // To limit the number of repaint requests, check if a certain amount of time has passed.
            if (EditorApplication.timeSinceStartup - _lastRepaintTime > 0.5) // Updates twice a second
            {
                _lastRepaintTime = EditorApplication.timeSinceStartup;
                this.Repaint();
            }
        }
        public override void OnInspectorGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.LabelField("Reminders", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Initialize: 'StartClockReporter()'\nStart the timer: 'StartTimer()'", MessageType.Info);
            }
                // Draw the default inspector options
                //DrawDefaultInspector();
            serializedObject.Update();
            _gameClock.TheReporterDetails = (FP_Stat_Type)EditorGUILayout.ObjectField("TheReporterDetails", _gameClock.TheReporterDetails, typeof(FP_Stat_Type), false);
            _gameClock.StatCollector = (FP_StatReporter)EditorGUILayout.ObjectField("StatCollector", _gameClock.StatCollector, typeof(FP_StatReporter), true);
            //EditorGUILayout.PropertyField(SOStatCollector);
            //EditorGUILayout.PropertyField(SOReporterDetails);
            _gameClock.EventDetails = EditorGUILayout.TextField("Event Details", _gameClock.EventDetails);
            //EditorGUILayout.PropertyField(EventDetails);
            //multiplier = EditorGUILayout.FloatField(multiplier);
            // Ensure the game clock script is not null
            if (_gameClock == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Game Clock Info", EditorStyles.boldLabel);

            // Display the running time if the game is in play mode
            if (Application.isPlaying)
            {
                var statReporter = _gameClock.ReturnStatReporter() as FP_StatReporter_Float;
                if(statReporter != null)
                {
                    if (statReporter.TheStatData != null)
                    {
                        return;
                    }
                    if (statReporter.TheStatData.StatSize() > 0)
                    {
                        if (_gameClock.TimerFinished)
                        {
                            //done
                            var timeWithoutPause = statReporter.TheStatData.StatReportLastItem().EventTime - statReporter.TheStatData.StatReportFirstItem().EventTime;
                            var runningTime = timeWithoutPause.TotalSeconds - _gameClock.ReturnPausedTime();
                            //TimeSpan runningTime = TimeSpan.FromSeconds(DateTime.Now - statReporter.TheStatData.StatReportFirstItem().EventTime - _gameClock.ReturnPausedTime());
                            //format double with two decimals

                            EditorGUILayout.LabelField("Running Time:", runningTime.ToString("F2"));
                            EditorGUILayout.LabelField("Paused Time:", _gameClock.ReturnPausedTime().ToString("F2"));

                        }
                        else
                        {
                            var timeWithoutPause = DateTime.Now - statReporter.TheStatData.StatReportFirstItem().EventTime;
                            var runningTime = timeWithoutPause.TotalSeconds - _gameClock.ReturnPausedTime();
                            //TimeSpan runningTime = TimeSpan.FromSeconds(DateTime.Now - statReporter.TheStatData.StatReportFirstItem().EventTime - _gameClock.ReturnPausedTime());
                            EditorGUILayout.LabelField("Running Time:", runningTime.ToString("F2"));
                            EditorGUILayout.LabelField("Paused Time:", _gameClock.ReturnPausedTime().ToString("F2"));
                        }
                        
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Running Time:", "0.0");
                        EditorGUILayout.LabelField("Paused Time:", "0.0");
                    }
                }
                EditorGUILayout.BeginHorizontal();

                if (!_gameClock.RunningClock && !_gameClock.Paused)
                {
                    if (GUILayout.Button("Start Timer"))
                    {
                        if (!_gameClock.ReturnStatReporterRunning())
                        {
                            //we need to force the reporter
                            _gameClock.StartClockReporter();
                        }
                        _gameClock.StartTimer();
                    }
                }
                else
                {
                    if (GUILayout.Button("End Timer"))
                    {
                        _gameClock.EndTimer();
                    }
                }

                if (!_gameClock.Paused)
                {
                    if (GUILayout.Button("Pause Timer"))
                    {
                        _gameClock.PauseTimer();
                    }
                }
                else
                {
                    if (GUILayout.Button("Restart Timer"))
                    {
                        _gameClock.UnPauseTimer();
                    }
                }
                

               

                EditorGUILayout.EndHorizontal();
                EditorUtility.SetDirty(_gameClock);
            }
            else
            {
                EditorGUILayout.HelpBox("Running and paused time will be displayed when the game is playing.", MessageType.Info);
            }
        }
    }
    */
}
