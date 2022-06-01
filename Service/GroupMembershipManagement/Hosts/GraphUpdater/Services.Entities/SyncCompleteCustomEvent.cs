// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System;
using System.Collections.Generic;

namespace Services.Entities
{
    public class SyncCompleteCustomEvent
    {
        public string TargetOfficeGroupId { get; set; }
        public string RunId { get; set; }
        public string IsDryRunEnabled { get; set; }
        public string ProjectedMemberCount { get; set; }
        public string MembersToAdd { get; set; }
        public string MembersToRemove { get; set; }
        public string IsInitialSync { get; set; }
        public string Result { get; set; }
        public string SyncJobTimeElapsedSeconds { get; set; }
    }
}
