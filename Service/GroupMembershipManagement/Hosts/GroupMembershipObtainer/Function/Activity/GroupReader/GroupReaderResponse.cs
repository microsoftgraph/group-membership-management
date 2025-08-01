// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupReaderResponse
    {
        public AzureADGroup SourceGroup { get; set; }
        public string SourceGroupId { get; set; }
    }
}
