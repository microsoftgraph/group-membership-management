// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.CustomAttributes;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class LogMessage
    {
        public Guid? InstanceId { get; set; }
        public string MessageTypeName { get; set; }
        public Guid? RunId { get; set; }
        public string Message { get; set; }

        [IgnoreLogging]
        public Dictionary<string, string> DynamicProperties { get; set; } = new Dictionary<string, string>();

        public Dictionary<string, string> ToDictionary()
        {
            var properties = DictionaryHelper.ToDictionary(this, new DictionaryHelper.Options { UseCamelCase = true });
            foreach(var property in DynamicProperties)
            {
                properties[property.Key] = property.Value;
            }

            return properties;
        }


    }
}
