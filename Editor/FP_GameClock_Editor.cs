using UnityEngine;
using UnityEditor;
using System;

namespace FuzzPhyte.Utility.Analytics.Editor
{
    [CustomEditor(typeof(FP_Stat_GameClock))]
    public class FP_GameClock_Editor : UnityEditor.Editor
    {
        private FP_Stat_GameClock _gameClock;
        SerializedProperty SOReporterDetails;
        SerializedProperty EventDetails;
        private double _lastRepaintTime;

        private void OnEnable()
        {
            _gameClock = (FP_Stat_GameClock)target;
            SOReporterDetails = serializedObject.FindProperty("TheReporterDetails");
            EventDetails = serializedObject.FindProperty("EventDetails");
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
            // Draw the default inspector options
            //DrawDefaultInspector();
            serializedObject.Update();
            EditorGUILayout.PropertyField(SOReporterDetails);
            EditorGUILayout.PropertyField(EventDetails);
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
}
