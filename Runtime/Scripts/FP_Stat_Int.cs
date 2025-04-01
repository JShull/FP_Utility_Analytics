using System;
using System.Collections.Generic;

namespace FuzzPhyte.Utility.Analytics
{
    public class FP_Stat_Int : FP_Stat<int,string>
    {
        private Func<int, double> conversionFunction = value => value;
        public FP_Stat_Int(FP_Stat_Type statData, List<StatCalculationType> calculationTypes) : base(statData, calculationTypes)
        {

        }
        public override string StatConversion(int data)
        {
            // Determine the number of digits in the input value
            int numDigits = (int)Math.Log10(data) + 1;

            // Calculate the number of leading zeros required
            int numLeadingZeros = Math.Max(0, 4 - numDigits);

            // Create a format string with the appropriate number of zeros
            string format = new string('0', numLeadingZeros + numDigits);

            // Convert the integer to a formatted string
            return data.ToString(format);
        }
        public override string ReturnConvertedDataByIndex(int index)
        {
            if (_statHistory.Count > index)
            {
                return StatConversion(_statHistory[index].Data);
            }
            else
            {
                return StatConversion(0);
            }
        }
        public override void StatEnd()
        {
            base.StatEnd();
            RunCalculators(conversionFunction);
        }
    }
}
