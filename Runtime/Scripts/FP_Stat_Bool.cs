using System;
using System.Collections.Generic;

namespace FuzzPhyte.Utility.Analytics
{
    public class FP_Stat_Bool : FP_Stat<bool,string>
    {
        Func<bool, double> conversionFunction = b => b ? 1.0 : 0.0;

        public FP_Stat_Bool(FP_Stat_Type statData, List<StatCalculationType> calculationTypes) : base(statData, calculationTypes)
        {

        }
        public override string StatConversion(bool data)
        {
            //return "True" if true and "False" if false
            return data ? "True" : "False";
        }
        public override string ReturnConvertedDataByIndex(int index)
        {
            if (_statHistory.Count > index)
            {
                return StatConversion(_statHistory[index].Data);
            }
            else
            {
                return StatConversion(false);
            }
        }
        public override void StatEnd()
        {
            base.StatEnd();
            RunCalculators(conversionFunction);
        }
    }
}
