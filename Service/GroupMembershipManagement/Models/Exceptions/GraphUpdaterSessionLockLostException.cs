// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics.CodeAnalysis;

namespace Models.Exceptions
{
	[ExcludeFromCodeCoverage]
	public class GraphUpdaterSessionLockLostException: Exception
	{

        Guid TargetOfficeGroupId;
        Guid RunId;
        int MessagesInSession;

        public GraphUpdaterSessionLockLostException(string message, Exception inner, Guid targetOfficeGroupId, Guid runId, int messagesInSession) 
            : base(message, inner)
        {
            TargetOfficeGroupId = targetOfficeGroupId;
            RunId = runId;
            MessagesInSession = messagesInSession;
        }
    }
}
