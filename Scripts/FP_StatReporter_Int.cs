using UnityEngine;

namespace FuzzPhyte.Utility.Analytics
{
    public class FP_StatReporter_Int : FP_StatReporter
    {
        FP_Stat_Int theStatData;
        public FP_Stat_Int TheStatData { get => theStatData; }
        public override void Start()
        {
            base.Start();
            //additional stuff on start
            if (StatReporter != null)
            {
                theStatData = new FP_Stat_Int(StatReporter, CalculationTypes);
                if (StartOnStart)
                {
                    theStatData.StatStart();
                }
            }
        }
        /// <summary>
        /// Best used for things that are singular countable events e.g. pick-ups
        /// </summary>
        /// <param name="data">Event data int stored, default is 1</param>
        /// <param name="details">Event Information</param>
        /// <param name="stored">Result of being added to the log or not</param>
        /// <returns></returns>
        public StatReportArgs<int> NewStatData(string details,ref bool stored, int data=1)
        {
            var newData = new StatReportArgs<int>(data, details);
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
        public string ReturnConvertedDataByInt(int data)
        {
            return theStatData.StatConversion(data);
        }
    }
}
