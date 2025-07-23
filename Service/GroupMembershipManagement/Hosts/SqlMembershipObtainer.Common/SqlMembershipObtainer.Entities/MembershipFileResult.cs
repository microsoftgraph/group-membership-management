// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace SqlMembershipObtainer.Entities
{
    public class MembershipFileResult
    {
        public SyncStatus Status { get; set; }
        public string FilePath { get; set; }
    }
}