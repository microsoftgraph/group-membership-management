// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.GraphUpdater
{
    public class GroupValidatorRequest
    {
        public Guid RunId { get; set; }
        public Guid GroupId { get; set; }
        public string JobPartitionKey { get; set; }
        public string JobRowKey { get; set; }
        public string AdaptiveCardTemplateDirectory { get; set; }
    }
}