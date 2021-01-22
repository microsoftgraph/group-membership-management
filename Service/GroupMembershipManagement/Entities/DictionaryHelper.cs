// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.CustomAttributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Entities
{
    public static class DictionaryHelper
    {
        public static Dictionary<string, string> ToDictionary<T>(this T input, Options options = null)  where T : class
        {
            var values = new Dictionary<string, string>();
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (options?.PropertiesToIgnore?.Contains(property.Name, StringComparer.InvariantCultureIgnoreCase) ?? false)
                {
                    continue;
                }

                var ignoreAttribute = property.GetCustomAttributes(typeof(IgnoreLoggingAttribute), true).FirstOrDefault();
                if (ignoreAttribute == null)
                {
                    var propertyName = property.Name;
                    if (options?.UseCamelCase ?? false)
                    {
                        propertyName = char.ToLower(propertyName[0]) + propertyName.Substring(1);
                    }

                    values.Add(propertyName, Convert.ToString(property.GetValue(input)));
                }
            }

            return values;
        }

        public class Options
        {
            public bool UseCamelCase { get; set; }
            public List<string> PropertiesToIgnore { get; set; }
        }
    }
}
