using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FuzzPhyte.Utility.Analytics
{
    /// <summary>
    /// Base class for keeping track of some various stat within a stack
    /// think of this like a running log of events T that happen in the game where we will convert T to A
    /// when it's necessary
    /// </summary>
    public abstract class FP_Stat<T,A> : IStat<T,A> where T: IConvertible
    {
        public FP_Stat_Type TheStat;
        protected bool StatIsRunning;
        public bool ReturnStatRunning { get => StatIsRunning;}
        protected List<StatCalculationType>possibleCalculationTypes;
        [Tooltip("Really used to have a base start time not from the events")]
        protected DateTime OverallStartTime;
        [Tooltip("Really used to have a base end time not from the events")]
        protected DateTime OverallEndTime;
        [Tooltip("Stack of events")]
        protected List<StatReportArgs<T>> _statHistory;
        protected Dictionary<StatCalculationType, double> _statCalculations;
        

        public FP_Stat(FP_Stat_Type statTypeData, List<StatCalculationType> calcTypes)
        {
            _statHistory = new List<StatReportArgs<T>>();
            possibleCalculationTypes = new List<StatCalculationType>(calcTypes);
            _statCalculations= new Dictionary<StatCalculationType, double>();
            //build the dictionary of our results
            for(int i=0;i<calcTypes.Count; i++)
            {
                var statCalc = calcTypes[i];
                if (!_statCalculations.ContainsKey(statCalc))
                {
                    _statCalculations.Add(statCalc, 0.0);
                }
                
            }
            StatIsRunning = false;
        }
        
        public virtual void StatStart() 
        { 
            OverallStartTime = DateTime.Now;
            StatIsRunning = true;
        }
        public virtual void StatEnd() 
        { 
            OverallEndTime = DateTime.Now;
            StatIsRunning = false;
        }
       
        public virtual bool StatNewEntry(StatReportArgs<T>newEntry)
        {
            if (StatIsRunning)
            {
                _statHistory.Add(newEntry);
                return true;
            }
            return false;
        }
        /// <summary>
        /// Just going to peek the last item in the stack
        /// </summary>
        /// <returns></returns>
        public virtual StatReportArgs<T> StatReportLastItem()
        {
            return _statHistory.LastOrDefault();
        }
        /// <summary>
        /// Just need to grab the first item in the stack
        /// </summary>
        /// <returns></returns>
        public virtual StatReportArgs<T> StatReportFirstItem()
        {
            return _statHistory.FirstOrDefault();
        }
        /// <summary>
        /// Going to wipe our internal Stack
        /// </summary>
        public virtual void StatReset()
        {
            _statHistory.Clear();
        }
        public virtual int StatSize()
        {
            return _statHistory.Count;    
        }
        /// <summary>
        /// Generic Conversion utilizing IConvertible
        /// we should 100% override this and make our own for our needs
        /// this implementation is just to have a 'generic' solution without an error
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        /// <exception cref="InvalidCastException"></exception>
        public virtual A StatConversion(T data)
        {
            try
            {
                return (A)Convert.ChangeType(data, typeof(A));
            }
            catch(InvalidCastException ex)
            {
                throw new InvalidCastException($"Cannot convert {typeof(T)} to {typeof(A)}, {ex.Message}");
            }
        }
        public virtual A ReturnConvertedDataByIndex(int index)
        {
            if (_statHistory.Count > index)
            {
                return StatConversion(_statHistory[index].Data);
            }
            return default(A);
        }
        /// <summary>
        /// Return the Theme
        /// </summary>
        /// <returns></returns>
        public virtual FP_Theme ReturnStatTheme()
        {
            if(TheStat != null)
            {
                if (TheStat.StatTheme != null)
                {
                    return TheStat.StatTheme;
                }
            }
            
            return null;
        }
        /// <summary>
        /// Return the possible calculation types
        /// </summary>
        /// <returns></returns>
        public virtual List<StatCalculationType> ReturnCalculationTypes()
        {
            if (possibleCalculationTypes.Count > 0)
            {
                return possibleCalculationTypes;
            }
            return null;
        }
        /// <summary>
        /// Update our final calculator results
        /// </summary>
        /// <param name="calculator"></param>
        /// <param name="results"></param>
        public virtual void UpdateCalculatorResults(StatCalculationType calculator, double results)
        {
            if (_statCalculations.ContainsKey(calculator))
            {
                _statCalculations[calculator] = results;
            }
            else
            {
                _statCalculations.Add(calculator, results);
            }
        }
        /// <summary>
        /// Run Calculator Results
        /// </summary>
        public virtual void RunCalculators(Func<T,double> conversionFunc)
        {
            for (int i = 0; i < possibleCalculationTypes.Count; i++)
            {
                var curCalculator = possibleCalculationTypes[i];
                double result = 0.0;

                switch (curCalculator)
                {
                    case StatCalculationType.Sum:
                        SumCalculator<T> sumCalc = new SumCalculator<T>(conversionFunc, StatCalculationType.Sum);
                        result = sumCalc.CalculateStat(_statHistory);
                        break;
                    case StatCalculationType.Average:
                        AverageCalculator<T> avgCalc = new AverageCalculator<T>(conversionFunc, StatCalculationType.Average);
                        result = avgCalc.CalculateStat(_statHistory);
                        break;
                    case StatCalculationType.AverageTimeBetweenEvents:
                        TimeBetweenCalculator<T> timeCalc = new TimeBetweenCalculator<T>(conversionFunc, StatCalculationType.AverageTimeBetweenEvents);
                        result = timeCalc.CalculateStat(_statHistory);
                        break;
                    case StatCalculationType.MaxEvent:
                        MaxEventCalculator<T> maxCalc = new MaxEventCalculator<T>(conversionFunc, StatCalculationType.MaxEvent);
                        result = maxCalc.CalculateStat(_statHistory);
                        //most likely this will be 1
                        break;
                    case StatCalculationType.MinEvent:
                        MinEventCalculator<T> minCalc = new MinEventCalculator<T>(conversionFunc, StatCalculationType.MinEvent);
                        result = minCalc.CalculateStat(_statHistory);
                        //most likely for this pickup int will be 1
                        break;
                    case StatCalculationType.StandardDeviation:
                        StandardDevCalculator<T> stdCalc = new StandardDevCalculator<T>(conversionFunc, StatCalculationType.StandardDeviation);
                        result = stdCalc.CalculateStat(_statHistory);
                        break;
                    case StatCalculationType.Variance:
                        VarianceCalculator<T> varCalc = new VarianceCalculator<T>(conversionFunc, StatCalculationType.Variance);
                        result = varCalc.CalculateStat(_statHistory);
                        break;
                    case StatCalculationType.TotalTime:
                        TotalTimeCalculator<T> ttCalc = new TotalTimeCalculator<T>(conversionFunc, StatCalculationType.TotalTime);
                        result = ttCalc.CalculateStat(_statHistory);
                        break;
                }
                UpdateCalculatorResults(curCalculator, result);
            }
        }
        public virtual double ReturnCalculatorResults(StatCalculationType calculator)
        {
            if (_statCalculations.ContainsKey(calculator))
            {
                return _statCalculations[calculator];
            }
            else
            {
                return 0.0;
            }
        }
        /// <summary>
        /// Return the stat data in it's raw form
        /// </summary>
        /// <returns></returns>
        public virtual List<StatReportArgs<T>> ReturnStatData()
        {
            return _statHistory;
        }
    }
}
