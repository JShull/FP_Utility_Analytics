using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FuzzPhyte.Utility.Analytics
{
    /// <summary>
    /// Argument for generic data type to pass back some sort of 'stat' data object
    /// in most cases this is a one parameter value, something like a float, int, double etc.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class StatReportArgs<T>
    {
        public DateTime EventTime;
        public string EventDetails;
        public T Data { get; private set; }
        public StatReportArgs(T data, string eventDetails)
        {
            Data = data;
            EventDetails = eventDetails;
            EventTime = DateTime.Now;
        }

        public static explicit operator StatReportArgs<T>(StatReportArgs<object> v)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// Just an enumeration to help contain the various types of stats we have for analysis purposes
    /// for every stat enum we need a corresponding calculator class that utilizes the IStatCalculator interface
    /// </summary>
    public enum StatCalculationType
    {
        Sum, //SumCalculator
        Average, //AverageCalculator
        AverageTimeBetweenEvents, //TimeBetweenCalculator
        MaxEvent, //MaxEventCalculator
        MinEvent, //MinEventCalculator
        StandardDeviation, //StandardDevCalculator
        Variance, //VarianceCalculator
        TotalTime
    }
    /// <summary>
    /// Used really in the UI when we want to make sure to pass the right value to the right location
    /// </summary>
    public enum SpecialStatType
    {
        None,
        TotalTime,
        GameScore,
        LevelValue
    }
    public static class StatCalculationFormat
    {
        public static string ReturnFormatStatType(StatCalculationType statType)
        {
            switch (statType)
            {
                case StatCalculationType.Sum:
                    return "Total";
                case StatCalculationType.Average:
                    return "Average";
                case StatCalculationType.AverageTimeBetweenEvents:
                    return "Avg Time Between";
                case StatCalculationType.MaxEvent:
                    return "Max Value";
                case StatCalculationType.MinEvent:
                    return "Min Value";
                case StatCalculationType.StandardDeviation:
                    return "StDev";
                case StatCalculationType.Variance:
                    return "Variance";
                case StatCalculationType.TotalTime:
                    return "Time";
            }
            return "";
        }
        public static string ReturnStandardTimeFormat(DateTime dateTime)
        {
            return dateTime.ToString("HH:mm:ss");
        }
        public static string ReturnStandardFormatDouble(double value)
        {
            return value.ToString("00.00");
        }

    }
    /// <summary>
    /// Base functions for FP_Stat to really 'work' figured an interface would be 'nice'
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="A"></typeparam>
    public interface IStat<T, A>
    {
        void StatStart();
        void StatEnd();
        bool StatNewEntry(StatReportArgs<T> args);
        StatReportArgs<T> StatReportLastItem();
        StatReportArgs<T> StatReportFirstItem();
        FP_Theme ReturnStatTheme();
        List<StatCalculationType> ReturnCalculationTypes();
        void StatReset();
        A StatConversion(T data);
    }
    /// <summary>
    /// Interface for representing a calculation strategy for a given stat
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IStatCalculator<T>
    {
        Func<T, double> ConversionFunction { get; set; }
        (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType);
    }
    /// <summary>
    /// Base class for a calculator
    /// this base class handles the function and error checking on the stack
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class AStatCalculator<T> : IStatCalculator<T>
    {
        /// <summary>
        /// Core value
        /// </summary>
        protected double statValue;
        protected StatCalculationType statType;
        public double ReturnStatValue { get { return statValue; } }
        public Func<T, double> ConversionFunction { get; set; }
        public AStatCalculator(Func<T, double> conversionFunction, StatCalculationType myStatType)
        {
            ConversionFunction = conversionFunction ?? throw new ArgumentNullException(nameof(conversionFunction));
            statType = myStatType;
            statValue = 0;
        }

        public virtual bool ValidCalculation(List<StatReportArgs<T>> statHistory)
        {
            if (statHistory.Count == 0)
            {
                Debug.WriteLine("Stat History is empty");
                return false;
                //throw new InvalidOperationException("Stat History is empty");
            }
            if (ConversionFunction == null)
            {
                Debug.WriteLine("Conversion Function is null");
                return false;
                //throw new InvalidOperationException("Conversion Function is null");
            }
            return true;
        }
        public virtual (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType)
        {
            throw new NotImplementedException();
        }
        

    }
    /// <summary>
    /// Sum Calculator class. This is going to return and calculate the sum of all events
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SumCalculator<T> : AStatCalculator<T>
    {
        public SumCalculator(Func<T, double> conversionFunction,StatCalculationType myType) : base(conversionFunction,myType)
        {
        }
        public override (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType = StatCalculationType.Sum)
        {
            if (!ValidCalculation(statHistory))
            {
                return (0, false);
                //throw new InvalidOperationException($"Cannot continue on with our sum calculator, see previous errors");
            }
            statValue = 0;
            foreach (var item in statHistory)
            {
                statValue += ConversionFunction(item.Data);
            }
            return (statValue,true);
        }
    }
    /// <summary>
    /// Average Calculator Class. This is going to return and calculate the average of all events
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AverageCalculator<T> : SumCalculator<T>
    {
        protected double avg;
        public double ReturnAverageValue { get { return avg; } }
        public AverageCalculator(Func<T, double> conversionFunction, StatCalculationType myType) : base(conversionFunction, myType)
        {
            avg = 0;
        }
        public override (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType = StatCalculationType.Average)
        {
            //reset values of interest
            statValue = 0;
            avg = 0;
            //base.CalculateStat(statHistory, StatCalculationType.Sum);
            var item = base.CalculateStat(statHistory, StatCalculationType.Sum);
            if (!item.Item2)
            {
                return (0, false);
            }
            avg = statValue / statHistory.Count;
            return (avg,true);
        }
    }
    /// <summary>
    /// This is going to return and calculate the average time between events
    /// going to derive from Average Calculator but override the CalculateStat function and not use the base function at all
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class TimeBetweenCalculator<T> : AverageCalculator<T>
    {
        public TimeBetweenCalculator(Func<T, double> conversionFunction, StatCalculationType myType) : base(conversionFunction,myType)
        {
        }
        public override (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType = StatCalculationType.AverageTimeBetweenEvents)
        {
            if (!ValidCalculation(statHistory))
            {
                return (0, false);
                //throw new InvalidOperationException($"Cannot continue on with our Time Between calculator, see previous errors");
            }
            statValue = 0;
            
            for (int i = 0; i < statHistory.Count; i++)
            {
                if (i == 0)
                {
                    continue;
                }
                statValue += (statHistory.ElementAt(i).EventTime - statHistory.ElementAt(i - 1).EventTime).TotalSeconds;
                Debug.WriteLine($"Time between events {statValue}");
            }
            avg = (statValue / (statHistory.Count-1));
            return (avg,true);
        }
    }
    /// <summary>
    /// Maximum Event Calculator
    /// assuming we pass the conversion function to convert T to a double
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class MaxEventCalculator<T> : AStatCalculator<T>
    {
        public MaxEventCalculator(Func<T, double> conversionFunction, StatCalculationType myType) : base(conversionFunction,myType)
        {
        }
        public override (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType = StatCalculationType.MaxEvent)
        {
            if (!ValidCalculation(statHistory))
            {
                return (0, false);
                //throw new InvalidOperationException($"Cannot continue on with our maximum calculator, see previous errors");
            }
            statValue = statHistory.Max(arg => ConversionFunction(arg.Data));
            return (statValue,true);
        }
    }
    /// <summary>
    /// Minimum Event Calculator
    /// assuming we pass the conversion function to convert T to a double
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class MinEventCalculator<T> : AStatCalculator<T>
    {
        public MinEventCalculator(Func<T, double> conversionFunction, StatCalculationType myType) : base(conversionFunction, myType)
        {
        }
        public override (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType = StatCalculationType.MinEvent)
        {
            if (!ValidCalculation(statHistory))
            {
                return (0.0, false);
                //throw new InvalidOperationException($"Cannot continue on with the minimum calculator, see previous errors");
            }
            statValue = statHistory.Min(arg => ConversionFunction(arg.Data));
            return (statValue,true);
        }
    }
    /// <summary>
    /// Standard Deviation calculator that extends our average calculator
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class StandardDevCalculator<T> : AverageCalculator<T>
    {
        protected double sumSquareDifferences;
        public double ReturnSumSquareDifference { get { return sumSquareDifferences; } }
        protected double standardDeviation;
        public double ReturnStandardDeviation { get { return standardDeviation; } }
        public StandardDevCalculator(Func<T, double> conversionFunction, StatCalculationType myType) : base(conversionFunction, myType)
        {
        }
        public override (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType = StatCalculationType.StandardDeviation)
        {
            //base.CalculateStat(statHistory, StatCalculationType.Average);
            var items = base.CalculateStat(statHistory, StatCalculationType.Average);
            if (!items.Item2)
            {
                return (0, false);
            }
            sumSquareDifferences = statHistory.Sum(item => Math.Pow(ConversionFunction(item.Data) - avg, 2));
            standardDeviation = Math.Sqrt(sumSquareDifferences / statHistory.Count);
            return (standardDeviation,true);
        }
    }
    /// <summary>
    /// Variance calculator that extends our standard deviation one
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class VarianceCalculator<T> : StandardDevCalculator<T>
    {
        protected double variance;
        public double ReturnVariance { get { return variance; } }
        public VarianceCalculator(Func<T, double> conversionFunction, StatCalculationType myType) : base(conversionFunction,myType)
        {
        }
        public override (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType = StatCalculationType.Variance)
        {
            //base.CalculateStat(statHistory, StatCalculationType.StandardDeviation);
            var items = base.CalculateStat(statHistory, StatCalculationType.StandardDeviation);
            if (!items.Item2)
            {
                return (0, false);
            }
            variance = sumSquareDifferences / statHistory.Count;
            return (variance,true);
        }
    }
    /// <summary>
    /// Total Time assumes first and last entry are valid
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class TotalTimeCalculator<T> : AStatCalculator<T>
    {
        public TotalTimeCalculator(Func<T, double> conversionFunction, StatCalculationType myType) : base(conversionFunction, myType)
        {
        }
        public override (double,bool) CalculateStat(List<StatReportArgs<T>> statHistory, StatCalculationType calcType = StatCalculationType.TotalTime)
        {
            if (!ValidCalculation(statHistory))
            {
                return (0, false);
            }
            
            var timeDiff = statHistory.Last().EventTime - statHistory.FirstOrDefault().EventTime;
            return (timeDiff.TotalSeconds,true);
        }
    }
    /// <summary>
    /// Weight Grade Calculator = assumes your calculators are okay
    /// and weights equate to 1.0
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class WeightedGradeCalculator<T>
    {
        private readonly List<(AStatCalculator<T> Calculator, double Weight)> _calculatorsWithWeights;
        protected StatCalculationType myWeightedType;

        public WeightedGradeCalculator(StatCalculationType WeightedType)
        {
            myWeightedType = WeightedType;
            _calculatorsWithWeights = new List<(AStatCalculator<T>, double)>();
        }
        /// <summary>
        /// Add a calculator to our Weighted Grade Calculator
        /// </summary>
        /// <param name="calculator"></param>
        /// <param name="weight"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void AddCalculator(AStatCalculator<T> calculator, double weight)
        {
            if (calculator == null)
            {
                throw new ArgumentNullException($"Calculator is null: ${nameof(calculator)}");
            }

            if (weight < 0)
            {
                throw new ArgumentException("Weight must be non-negative.", nameof(weight));
            }
            _calculatorsWithWeights.Add((calculator, weight));
        }
        /// <summary>
        /// Public accessor to get our current total weights
        /// </summary>
        /// <returns></returns>
        public double ReturnTotalWeights()
        {
            return _calculatorsWithWeights.Sum(cw => cw.Weight);
        }
        /// <summary>
        /// Only call this when you have already setup your calculators and have calculated their final values
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public double CalculateWeightedGrade()
        {

            // Check if total weights equal 1
            var weights = ReturnTotalWeights();
            if (Math.Abs(weights - 1) > 1e-10)
            {
                throw new InvalidOperationException($"Total weights of calculators must equal 1, you're at {weights}");
            }
            double weightedSum = 0;
            for (int i = 0; i < _calculatorsWithWeights.Count; i++)
            {
                var (calculator, weight) = _calculatorsWithWeights[i];
                //we assume that the calculator has already been setup and has already calculated it's final value
                //we need to just switch on the type of Weighted Grade we want to calculate
                //need to cast our calculator to the correct type
                //some of these make no sense as we would never really wanted a weighted value for standard deviation/variance etc. but hey here ya go!
                switch (myWeightedType)
                {
                    case StatCalculationType.Sum:
                        weightedSum += calculator.ReturnStatValue * weight;
                        break;
                    case StatCalculationType.Average:    
                        var avgCalc = (AverageCalculator<T>)calculator;
                        weightedSum += avgCalc.ReturnAverageValue * weight;
                        break;
                    case StatCalculationType.StandardDeviation:
                        var stdDevCalc = (StandardDevCalculator<T>)calculator;
                        weightedSum += stdDevCalc.ReturnStandardDeviation * weight;
                        break;
                    case StatCalculationType.Variance:
                        var varCalc = (VarianceCalculator<T>)calculator;
                        weightedSum += varCalc.ReturnVariance * weight;
                        break;
                    case StatCalculationType.MinEvent:
                        var minCalc = (MinEventCalculator<T>)calculator;
                        weightedSum+=minCalc.ReturnStatValue * weight;
                        break;
                    case StatCalculationType.MaxEvent:
                        var maxCalc = (MaxEventCalculator<T>)calculator;
                        weightedSum += maxCalc.ReturnStatValue * weight;
                        break;
                    default:
                        weightedSum += 0;
                        break;
                }
            }

            return weightedSum; 
        }

    }
}
