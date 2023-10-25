// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace SqlMembershipObtainer
{
    public class ManagerOrgReaderRequest
    {
        public string Filter { get; set; }
        public int Depth { get; set; }
        public SyncJob SyncJob { get; set; }
        public int PersonnelNumber { get; set; }
        public string TableName { get; set; }
    }
}