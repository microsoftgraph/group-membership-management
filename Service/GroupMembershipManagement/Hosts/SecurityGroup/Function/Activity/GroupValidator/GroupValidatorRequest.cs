// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;

namespace Hosts.SecurityGroup
{
    public class GroupValidatorRequest
    {
        public Guid RunId { get; set; }
        public Guid ObjectId { get; set; }
        public SyncJob SyncJob { get; set; }
        public string Content { get; set; }
        public string[] AdditionalContentParams { get; set; }
        public string AdaptiveCardTemplateDirectory { get; set; }
    }
}