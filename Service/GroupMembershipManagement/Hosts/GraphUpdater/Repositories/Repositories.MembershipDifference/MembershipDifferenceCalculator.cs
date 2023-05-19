// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Repositories.MembershipDifference
{
	public class MembershipDifferenceCalculator<T> : IMembershipDifferenceCalculator<T> where T : IAzureADObject
	{
		public MembershipDelta<T> CalculateDifference(IEnumerable<T> source, IEnumerable<T> destination)
		{
			HashSet<T> sourceSet = new HashSet<T>(source), destSet = new HashSet<T>(destination);

			sourceSet.ExceptWith(destination);
			destSet.ExceptWith(source);

			return new MembershipDelta<T>(sourceSet, destSet);
		}
	}

}
