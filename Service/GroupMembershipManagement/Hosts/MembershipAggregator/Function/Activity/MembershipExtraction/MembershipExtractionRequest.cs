// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.MembershipAggregator
{
    public class MembershipExtractionRequest
    {
        private List<string> _completedParts = new();
        public List<string> CompletedParts
        {
            get => _completedParts;
            set => _completedParts = value ?? new();
        }
        public string DestinationPart { get; init; }
        public SyncJob SyncJob { get; init; }
        public Guid GroupId { get; set; }
        public DateTime CurrentUtcDateTime { get; set; }
    }
}
