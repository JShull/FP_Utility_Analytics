using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FuzzPhyte.Utility.Analytics
{
    public class FP_StatReporter_Float : FP_StatReporter
    {
        FP_Stat_Float theStatData;

        public FP_Stat_Float TheStatData { get => theStatData;}

        public override void Start()
        {
            base.Start();
            //additional stuff on start here
            if (StatReporter != null)
            {
                theStatData = new FP_Stat_Float(StatReporter, CalculationTypes);
                if (StartOnStart)
                {
                    theStatData.StatStart();
                }
            }
        }
        /// <summary>
        /// Start the stat manually
        /// </summary>
        public void StartTheStat()
        {
            theStatData.StatStart();
        }
        /// <summary>
        /// Best used for items that are not singular but could be countable and/or have a 0.0 value e.g. time
        /// </summary>
        /// <param name="data">Event data float stored, default is 1.0</param>
        /// <param name="details">Event Information</param>
        /// <param name="stored">Result of being added to the log or not</param>
        /// <returns></returns>
        public StatReportArgs<float> NewStatData(string details, ref bool stored, float data = 1f)
        {
            var newData = new StatReportArgs<float>(data, details);
            stored = theStatData.StatNewEntry(newData);
            //Debug.LogWarning($"Data correctly accepted? {stored} and event time stamp is {newData.EventTime}");
            return newData;
        }
        /// <summary>
        /// connect back to our data pattern to end the data collection process and then run the base mono end
        /// </summary>
        public override void EndStatData()
        {
            theStatData.StatEnd();
            base.EndStatData();
        }
        /// <summary>
        /// Returns from our data object the stat calculation by type
        /// </summary>
        /// <param name="calcType">the calculator type</param>
        /// <returns></returns>
        public override (double,bool) ReturnStatCalculation(StatCalculationType calcType)
        {
            return theStatData.ReturnCalculatorResults(calcType);
        }
        /// <summary>
        /// Returns the data by index from the data class as a string
        /// <paramref name="index"/>index value</param>
        /// </summary>
        public string ReturnConvertedDataByIndex(int index)
        {
            return theStatData.ReturnConvertedDataByIndex(index);
        }
        /// <summary>
        /// If we want the converted data by the data class
        /// </summary>
        /// <param name="data">data to convert</param>
        /// <returns></returns>
        public string ReturnConvertedDataByFloat(float data)
        {
            return theStatData.StatConversion(data);
        }
    }
}
