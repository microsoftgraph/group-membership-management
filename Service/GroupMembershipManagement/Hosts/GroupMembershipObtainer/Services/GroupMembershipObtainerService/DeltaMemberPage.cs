// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Carries one incremental membership call and the counts computed by the Graph repository.
    /// </summary>
    public sealed class DeltaMemberPage
    {
        public List<AzureADUser> UsersToAdd { get; set; }
        public List<AzureADUser> UsersToRemove { get; set; }
        public MemberReadCounts Counts { get; set; }
        public string NextPageUrl { get; set; }
        public string DeltaUrl { get; set; }
    }
}
