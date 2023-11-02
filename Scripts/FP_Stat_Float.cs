using System;
using System.Collections.Generic;

namespace FuzzPhyte.Utility.Analytics
{
    public class FP_Stat_Float : FP_Stat<float, string>
    {
        private Func<float, double> conversionFunction = value => value;
        public FP_Stat_Float(FP_Stat_Type statData, List<StatCalculationType> calculationTypes) : base(statData, calculationTypes)
        {

        }

        public override string StatConversion(float data)
        {
            // Format the float with one decimal place and leading zeros up to 4 characters
            return data.ToString("F1").PadLeft(4, '0');
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
