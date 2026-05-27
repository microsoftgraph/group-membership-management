// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace SqlMembershipObtainer.Entities
{
    public class TableNameResult
    {
        public string TableName { get; set; }
        public Guid? AdfRunId { get; set; }
    }
}
