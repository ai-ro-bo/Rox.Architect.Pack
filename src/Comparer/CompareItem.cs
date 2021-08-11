using System.Collections.Generic;

namespace Rox
{
    public class CompareItem
    {
        private CompareItem(CompareChange change, IDictionary<string, object> keyPropertyValues, IDictionary<string, CompareProperty> changePropertyValues = null)
        {
            Change = change;
            KeyPropertyValues = keyPropertyValues;
            ChangePropertyValues = changePropertyValues;
        }

        public CompareChange Change { get; }

        public IDictionary<string, object> KeyPropertyValues { get; }

        public IDictionary<string, CompareProperty> ChangePropertyValues { get; }

        //public static CompareItem CreateNone(IDictionary<string, object> keyPropertyValues) => new CompareItem(CompareChange.None, keyPropertyValues);

        public static CompareItem CreateAdd(IDictionary<string, object> keyPropertyValues, IDictionary<string, CompareProperty> changePropertyValues) => new CompareItem(CompareChange.Add, keyPropertyValues, changePropertyValues);

        public static CompareItem CreateUpdate(IDictionary<string, object> keyPropertyValues, IDictionary<string, CompareProperty> changePropertyValues) => new CompareItem(CompareChange.Update, keyPropertyValues, changePropertyValues);

        public static CompareItem CreateDelete(IDictionary<string, object> keyPropertyValues) => new CompareItem(CompareChange.Delete, keyPropertyValues);
    }
}