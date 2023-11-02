// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GraphUpdater
{
    public class EmailSenderRequest
    {
        public SyncJob SyncJob { get; set; }
        public string ToEmail { get; set; }
        public string ContentTemplate { get; set; }
        public string[] AdditionalContentParams { get; set; }
        public string CcEmail { get; set; }
        public string AdaptiveCardTemplateDirectory { get; set; }
    }
}