// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
	public class DeltaLinkUserReaderRequest
	{
        public Guid GroupId { get; set; }
        public Guid TargetGroupId { get; set; }
        public int CurrentPart { get; set; }
        public string DeltaLink { get; set; }
        public int NumberOfPages { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}