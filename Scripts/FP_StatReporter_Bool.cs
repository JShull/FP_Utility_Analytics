using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FuzzPhyte.Utility.Analytics
{
    /// <summary>
    /// This is a boolean statistical reporter that works hand in hand with a FP_Stat_Bool data class
    /// Like a light weight humble object version where this deals with the 'mono' side and our data class deals with the data
    /// </summary>
    public class FP_StatReporter_Bool : FP_StatReporter
    {
        FP_Stat_Bool theStatData;
        public FP_Stat_Bool TheStatData { get => theStatData; }

        public override void Start()
        {
            base.Start();
            //additional stuff on start here
            if (StatReporter != null)
            {
                theStatData = new FP_Stat_Bool(StatReporter, CalculationTypes);
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
        /// Best used for things that are singular countable events e.g. pick-ups
        /// </summary>
        /// <param name="data">Event data int stored, default is 1</param>
        /// <param name="details">Event Information</param>
        /// <param name="stored">Result of being added to the log or not</param>
        /// <returns></returns>
        public StatReportArgs<bool> NewStatData(string details, ref bool stored, bool data = false)
        {
            var newData = new StatReportArgs<bool>(data, details);
            stored = theStatData.StatNewEntry(newData);
            return newData;
        }
        /// <summary>
        /// connect back to our data pattern to end the data collection process and then run the base mono end
        /// </summary>
        public override void EndStatData()
        {
            //end mine first then run the base for the other integrated stuff
            theStatData.StatEnd();
            base.EndStatData();
        }
        /// <summary>
        /// Returns from our data object the stat calculation by type
        /// </summary>
        /// <param name="calcType">the calculator type</param>
        /// <returns></returns>
        public override double ReturnStatCalculation(StatCalculationType calcType)
        {
            return theStatData.ReturnCalculatorResults(calcType);
        }
        /// <summary>
        /// Returns the data by index from the data class
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public string ReturnConvertedDataByIndex(int index)
        {
            return theStatData.ReturnConvertedDataByIndex(index);
        }
        /// <summary>
        /// Conversion function to return the data as a string
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public string ReturnConvertedDataByBool(bool data)
        {
            return theStatData.StatConversion(data);
        }
    }
}
