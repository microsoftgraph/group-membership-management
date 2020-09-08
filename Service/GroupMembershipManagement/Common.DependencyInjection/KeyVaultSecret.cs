// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text;

namespace Common.DependencyInjection
{
	public class KeyVaultSecret<T> : IKeyVaultSecret<T>
	{
		public string Secret { get; set; }
	}
}

