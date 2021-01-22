// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Repositories.SecurityGroupCrawler
{
	public class SecurityGroupCycle
	{
		public AzureADGroup Group { get; private set; }
		public ImmutableList<AzureADGroup> Cycle { get; private set; }

		public SecurityGroupCycle(AzureADGroup group, ImmutableList<AzureADGroup> cycle)
		{
			Group = group;
			Cycle = cycle;
		}

		public override string ToString()
		{
			if (Cycle.Count == 1)
				return $"{Group} contains itself.";
			return string.Join(" -> ", Cycle) + " -> " + Group;
		}

	}
}
