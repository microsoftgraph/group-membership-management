// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Carries one transitive-members page and the counts computed by the Graph repository.
    /// </summary>
    public sealed class GroupMemberPage
    {
        public List<AzureADUser> Users { get; set; }
        public MemberReadCounts Counts { get; set; }
        public string NextPageUrl { get; set; }
    }
}
