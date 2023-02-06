// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Repositories.Mocks
{
	public class TestObjectHelpers
	{
		public static Random Rand { get; private set; } = new Random();
		private readonly List<Guid> _groupIds  = MakeIds(6);
		private readonly List<Guid> _userIds = MakeIds(6);

		public static T RandomSample<T>(T[] col)
		{
			return col[Rand.Next(col.Length)];
		}

		public AzureADGroup GroupNamed(int id)
		{
			while (_groupIds.Count < id + 1)
			{
				_groupIds.Add(Guid.NewGuid());
			}
			return new AzureADGroup { ObjectId = _groupIds[id] };
		}

		public AzureADUser UserNamed(int id)
		{
			while (_userIds.Count < id + 1)
			{
				_userIds.Add(Guid.NewGuid());
			}
			return new AzureADUser { ObjectId = _userIds[id] };
		}

		private static List<Guid> MakeIds(int size)
		{
			return Enumerable.Range(0, size).Select(_ => Guid.NewGuid()).ToList();
		}
	}
}
