using System.Collections;
using UnityEngine;

namespace FuzzPhyte.Utility.Analytics
{
    /// <summary>
    /// Event component that goes on a game object that will report a stat event
    /// assume that there's a FP_StatManager.Instance out there
    /// </summary>
    [System.Serializable]
    public class FP_Stat_Event: MonoBehaviour
    {
        //[Header("Variables that are set in the editor and required")]
        public string EventDetails;
        public FP_Stat_Type TheReporterDetails;
        [Header("Variables that are set at runtime")]
        [SerializeField]
        protected FP_StatReporter StatCollector;
        
        public virtual void Start()
        {
            StartCoroutine(EndOFFrame());
        }
        public virtual IEnumerator EndOFFrame()
        {
            yield return new WaitForEndOfFrame();
            StatCollector = FP_StatManager.Instance.ReturnSpecificStatCollector(TheReporterDetails);
            FP_StatManager.Instance.RegisterAStatEvent(this);
        }
        
        /// <summary>
        /// Outside Accessor for reporting a new stat event
        /// works on the StatImmutable type and we use defaults for data values by immutable type
        /// e.g. int = pickups = 1 pickup at event time now
        /// </summary>
        public virtual void NewStatEventReported()
        {
            bool dataSuccess=false;
            switch (TheReporterDetails.ImmutableStatType)
            {
                case StatImmutable.StatInt:
                    var intStat = StatCollector as FP_StatReporter_Int;
                    var intResult= intStat.NewStatData(EventDetails, ref dataSuccess);
                    if (dataSuccess)
                    {
                        Debug.LogWarning($"Adding event {EventDetails} and converted formatted data entry= {intStat.ReturnConvertedDataByInt(intResult.Data)}");
                    }
                    break;
                case StatImmutable.StatBool:
                    var boolStat = StatCollector as FP_StatReporter_Bool;
                    var boolResult = boolStat.NewStatData(EventDetails, ref dataSuccess);
                    if(dataSuccess)
                    {
                        Debug.LogWarning($"Adding event {EventDetails} and converted formatted data entry= {boolStat.ReturnConvertedDataByBool(boolResult.Data)}");
                    }
                    break;
                case StatImmutable.StatFloat:
                    var floatStat = StatCollector as FP_StatReporter_Float;
                    var floatResult = floatStat.NewStatData(EventDetails, ref dataSuccess);
                    if (dataSuccess)
                    {
                        Debug.LogWarning($"Adding event {EventDetails} and converted formatted data entry= {floatStat.ReturnConvertedDataByFloat(floatResult.Data)}");
                    }
                    else
                    {
                        Debug.LogWarning($"Data failed to be added to the stat collection for {this.gameObject.name}");
                    }
                    
                    break;
            }
        }
        /// <summary>
        /// Outside Accessor for reporting a new stat event with passed parameter int type
        /// </summary>
        /// <param name="value">int value</param>
        public virtual void NewStatEventReported(int value)
        {
            bool dataSuccess = false;
            switch (TheReporterDetails.ImmutableStatType)
            {
                case StatImmutable.StatInt:
                    var intStat = StatCollector as FP_StatReporter_Int;
                    var dataResult = intStat.NewStatData(EventDetails, ref dataSuccess, value);
                    if (dataSuccess)
                    {
                        Debug.LogWarning($"Adding event {EventDetails} and converted formatted data entry= {intStat.ReturnConvertedDataByInt(dataResult.Data)}");
                    }

                    break;
            }
        }
        public virtual void NewStatEventReported(bool value)
        {
            bool dataSuccess = false;
            switch (TheReporterDetails.ImmutableStatType) {

                case StatImmutable.StatBool:
                    var boolStat = StatCollector as FP_StatReporter_Bool;
                    var boolResult = boolStat.NewStatData(EventDetails, ref dataSuccess,value);
                    if (dataSuccess)
                    {
                        Debug.LogWarning($"Adding event {EventDetails} and converted formatted data entry= {boolStat.ReturnConvertedDataByBool(boolResult.Data)}");
                    }
                    break;
            }
        }
        /// <summary>
        /// Outside Accessor for reporting a new stat event with a passed parameter float type
        /// </summary>
        /// <param name="value">float value</param>
        
        public virtual void NewStatEventReported(float value)
        {
            bool dataSuccess = false;
            switch (TheReporterDetails.ImmutableStatType)
            {
                case StatImmutable.StatFloat:
                    var floatStat = StatCollector as FP_StatReporter_Float;
                    var floatResult = floatStat.NewStatData(EventDetails, ref dataSuccess, value);
                    if (dataSuccess)
                    {
                        Debug.LogWarning($"Adding event {EventDetails} and converted formatted data entry= {floatStat.ReturnConvertedDataByFloat(floatResult.Data)}");
                    }

                    break;
            }
        }
        /// <summary>
        /// wrapper for Unity Event System
        /// </summary>
        /// <param name="value"></param>
        public virtual void NewStatEventReported(FPFloatEvent value)
        {
            NewStatEventReported(value.Value);
        }
        /// <summary>
        /// Quick accessor for returning the stat reporter
        /// </summary>
        /// <returns></returns>
        public virtual FP_StatReporter ReturnStatReporter()
        {
            return StatCollector;
        }
        public virtual void EndStat()
        {
            switch (TheReporterDetails.ImmutableStatType)
            {
                case StatImmutable.StatInt:
                    var intStat = StatCollector as FP_StatReporter_Int;
                    intStat.EndStatData();
                    for (int i = 0; i < StatCollector.CalculationTypes.Count; i++)
                    {
                        //var curData = intStat.ReturnStatCalculation(StatCollector.CalculationTypes[i]);
                        Debug.LogWarning($"Stat Collected Type {StatCollector.CalculationTypes[i].ToString()} with a value of {intStat.ReturnStatCalculation(StatCollector.CalculationTypes[i])}");
                    }
                    break;
                case StatImmutable.StatFloat:
                    var floatStat = StatCollector as FP_StatReporter_Float;
                    floatStat.EndStatData();
                    for (int i = 0; i < StatCollector.CalculationTypes.Count; i++)
                    {
                        //var curData = intStat.ReturnStatCalculation(StatCollector.CalculationTypes[i]);
                        Debug.LogWarning($"Stat Collected Type {StatCollector.CalculationTypes[i].ToString()} with a value of {floatStat.ReturnStatCalculation(StatCollector.CalculationTypes[i])}");
                    }
                    break;
                case StatImmutable.StatBool:
                    var boolStat = StatCollector as FP_StatReporter_Bool;
                    boolStat.EndStatData();
                    for (int i = 0; i < StatCollector.CalculationTypes.Count; i++)
                    {
                        //var curData = intStat.ReturnStatCalculation(StatCollector.CalculationTypes[i]);
                        Debug.LogWarning($"Stat Collected Type {StatCollector.CalculationTypes[i].ToString()} with a value of {boolStat.ReturnStatCalculation(StatCollector.CalculationTypes[i])}");
                    }
                    break;
            }
        }
    }
}
