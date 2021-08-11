namespace Rox
{
    public class CompareProperty
    {
        public CompareProperty(object value)
        {
            Value = value;
        }

        public object Value { get; }

        public static CompareProperty CreateKey(object value) => new CompareProperty(value);

        public static CompareProperty CreateAdd(object value) => new CompareProperty(value);

        public static CompareProperty CreateUpdate(object value, object oldValue) => new CompareUpdateProperty(value, oldValue);
    }

    public class CompareUpdateProperty
            : CompareProperty
    {
        public CompareUpdateProperty(object value, object oldValue = null)
            : base(value)
        {
            OldValue = oldValue;
        }

        public object OldValue { get; }
    }
}