// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Entities;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterRequest : GraphUpdaterRequestBase
    {
        public RequestType Type { get; set; }
        public ICollection<AzureADUser> Members { get; set; }
        public bool IsInitialSync { get; set; }
        public int? TotalMemberCount { get; set; }
        public bool IsMultiLaneEnabled { get; set; }
    }
}