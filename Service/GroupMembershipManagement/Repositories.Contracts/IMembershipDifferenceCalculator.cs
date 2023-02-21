// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Repositories.Contracts
{
	public interface IMembershipDifferenceCalculator<T> where T : IAzureADObject
	{
		public MembershipDelta<T> CalculateDifference(IEnumerable<T> source, IEnumerable<T> destination);
	}

	public class MembershipDelta<T> where T : IAzureADObject
	{
		public ICollection<T> ToAdd { get; private set; }
		public ICollection<T> ToRemove { get; private set; }

		public MembershipDelta(ICollection<T> add, ICollection<T> remove)
		{
			ToAdd = add;
			ToRemove = remove;
		}

	}
}
