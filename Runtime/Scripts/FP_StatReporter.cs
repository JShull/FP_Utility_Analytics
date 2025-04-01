using System.Collections.Generic;
using UnityEngine;

namespace FuzzPhyte.Utility.Analytics
{
    /// <summary>
    /// Base Mono Stat for registering a stat to a game object
    /// based on the delegate for ending the 'stat' data
    /// this should only be derived from and not used directly
    /// </summary>
    public abstract class FP_StatReporter : MonoBehaviour
    {
        [Header("Stat Specifics")]
        [Tooltip("If we want to activate the reporter on start")]
        public bool StartOnStart;
        public FP_Stat_Type StatReporter;
        public List<StatCalculationType> CalculationTypes = new List<StatCalculationType>();
        public delegate void EndStatDataEventHandler();
        public event EndStatDataEventHandler EndStatDataEvent;
        public FP_StatManager FPStatManager;
        public virtual void Start()
        {
            if (FPStatManager == null)
            {
                if (FP_StatManager.Instance == null)
                {
                    var theManager = new GameObject("StatManager");
                    theManager.AddComponent<FP_StatManager>();
                    theManager.GetComponent<FP_StatManager>().RegisterStatCollector(StatReporter, this);
                    Debug.Log($"{gameObject.name}: Didn't find a FP_StatManager Instance, made my own and registered: Registered Stat Collector on Start");
                }
                else
                {
                    Debug.Log($"{gameObject.name}: Found an FP_StatManager instance and registered Stat collector");
                    FP_StatManager.Instance.RegisterStatCollector(StatReporter, this);
                }
            }
            else
            {
                Debug.Log($"{gameObject.name}: registered with the FP_StatManager");
                FPStatManager.RegisterStatCollector(StatReporter, this);
            }
        }
        
        public virtual void EndStatData()
        {
            EndStatDataEvent?.Invoke();
        }
        /// <summary>
        /// This is where you'd setup your calcuation by type
        /// </summary>
        /// <param name="calcType"></param>
        /// <returns></returns>
        public virtual (double,bool) ReturnStatCalculation(StatCalculationType calcType)
        {
            return (0,false);
        }
    }

}
