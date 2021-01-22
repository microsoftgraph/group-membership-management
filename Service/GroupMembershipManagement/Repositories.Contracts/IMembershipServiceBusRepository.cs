// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.ServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
	public interface IMembershipServiceBusRepository
	{
		Task SendMembership(GroupMembership groupMembership, string sentFrom = "");
	}
}
