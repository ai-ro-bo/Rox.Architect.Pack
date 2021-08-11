using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Rox
{
    public static class PropertyExtension
    {
        public static IEnumerable<PropertyInfo> GetPropertyInfos(this Type itemType)
        {
            //TODO: Test Guid, DateTime, TimeSpan
            IEnumerable<PropertyInfo> propertyInfos = itemType.GetProperties().Where(propertyInfo =>
                propertyInfo.CanRead && propertyInfo.CanWrite &&
                (propertyInfo.PropertyType.IsValueType || propertyInfo.PropertyType == typeof(string)));
            return propertyInfos;
        }

        public static IEnumerable<PropertyInfo> GetKeyPropertyInfos(this IEnumerable<PropertyInfo> propertyInfos, IEnumerable<string> keyPropertyNames)
        {
            ICollection<PropertyInfo> keyPropertyInfos = new List<PropertyInfo>();
            if (keyPropertyNames != null)
            {
                foreach (string keyPropertyName in keyPropertyNames)
                {
                    PropertyInfo keyPropertyInfo = propertyInfos.FirstOrDefault(propertyInfo => propertyInfo.Name == keyPropertyName);

                    if (keyPropertyInfo != null) keyPropertyInfos.Add(keyPropertyInfo);
                }
            }
            return keyPropertyInfos;
        }

        public static IDictionary<string, object> GetPropertyValues(this object item, IEnumerable<PropertyInfo> propertyInfos)
        {
            IDictionary<string, object> propertyValues = new Dictionary<string, object>();
            foreach (PropertyInfo propertyInfo in propertyInfos)
            {
                object propertyValue = propertyInfo.GetValue(item);

                propertyValues.Add(propertyInfo.Name, propertyValue);
            }
            return propertyValues;
        }

        public static void SetPropertyValues(this object item, IEnumerable<PropertyInfo> propertyInfos, IDictionary<string, object> propertyValues)
        {
            foreach (PropertyInfo propertyInfo in propertyInfos)
            {
                string propertyName = propertyInfo.Name;
                object propertyValue = null;
                if (propertyValues.ContainsKey(propertyName))
                {
                    propertyValue = propertyValues[propertyName];
                }
                try
                {
                    propertyInfo.SetValue(item, propertyValue);
                }
                catch { }
            }
        }

        public static T FindItem<T>(this IEnumerable<T> items, IEnumerable<PropertyInfo> keyPropertyInfos, IDictionary<string, object> keyPropertyValues)
        {
            T returnItem = default;
            foreach (T item in items)
            {
                IDictionary<string, object> keyItemPropertyValues = item.GetPropertyValues(keyPropertyInfos);
                if (keyItemPropertyValues.CompareKeyValues(keyPropertyValues))
                {
                    returnItem = item;
                    break;
                }
            }
            return returnItem;
        }

        public static bool CompareKeyValues(this IDictionary<string, object> keyItemPropertyValues, IDictionary<string, object> keyPropertyValues)
        {
            bool areEquals = true;
            foreach (KeyValuePair<string, object> keyItemProperty in keyItemPropertyValues)
            {
                object keyItemValue = null;
                if (keyPropertyValues.ContainsKey(keyItemProperty.Key))
                {
                    keyItemValue = keyPropertyValues[keyItemProperty.Key];
                }

                if (!Equals(keyItemValue, keyItemProperty.Value))
                {
                    areEquals = false;
                    break;
                }
            }
            return areEquals;
        }

        public static void UpdateByReference<T>(this IList<T> baseItems, IEnumerable<T> newItems)
        {
            int baseIndex = 0;
            foreach (T newItem in newItems)
            {
                T baseIndexItem = baseItems[baseIndex];
                if (!ReferenceEquals(newItem, baseIndexItem))
                {
                    if (baseItems.Contains(newItem))
                    {
                        baseItems.Remove(newItem);

                        if (baseIndex < baseItems.Count)
                        {
                            baseItems.Insert(baseIndex, newItem);
                        }
                        else
                        {
                            baseItems.Add(newItem);
                        }
                    }
                    else
                    {
                        if (baseIndex < baseItems.Count)
                        {
                            baseItems.Insert(baseIndex, newItem);
                        }
                        else
                        {
                            baseItems.Add(newItem);
                        }
                    }
                }
                baseIndex++;
            }

            //Note: If we are always moving items then this is faster delete routine
            int baseCount = baseItems.Count;
            if (baseIndex < baseCount)
            {
                for (int removeIndex = baseCount - 1; baseIndex < removeIndex; removeIndex--)
                {
                    //TODO: Testing! - REMOVE
                    int baseLastIndex = baseItems.Count - 1;
                    if (removeIndex != baseLastIndex) throw new Exception($"ACHTUNG! removeIndex {removeIndex} != (baseItems.Count - 1) {baseLastIndex}");

                    baseItems.RemoveAt(removeIndex);
                }
            }

            ////Note: Alternate delete routine works for move true/false
            //for (int deleteIndex = baseItems.Count - 1; deleteIndex < 0; deleteIndex--)
            //{
            //    if (!newItems.Contains(baseItems[deleteIndex]))
            //    {
            //        baseItems.RemoveAt(deleteIndex);
            //    }
            //}

            //TODO: Testing! - REMOVE
            int baseItemCount = baseItems.Count;
            int newItemCount = newItems.Count();
            if (baseItemCount != newItemCount) throw new Exception($"ACHTUNG! baseItems.Count {baseItemCount} != newItems.Count() {newItemCount}");
        }
    }
}