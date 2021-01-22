// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
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

        public Dictionary<string, string> ToDictionary() =>
            DictionaryHelper.ToDictionary(this, new DictionaryHelper.Options { UseCamelCase = true });

    }
}
