// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics.CodeAnalysis;

namespace SqlMembershipObtainer.Entities
{
	[ExcludeFromCodeCoverage]
	public class SqlMembershipObtainerSQLException : Exception
	{

        Guid? TargetOfficeGroupId;
        Guid? RunId;
        
        public SqlMembershipObtainerSQLException(string message, Exception inner, Guid? targetOfficeGroupId, Guid? runId) : base(message, inner)
        {
            TargetOfficeGroupId = targetOfficeGroupId;
            RunId = runId;
        }
    }
}
