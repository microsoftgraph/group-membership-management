// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Common.DependencyInjection
{
	public class GraphCredentials
	{
		public string TenantId { get; set; }
		public string ClientId { get; set; }
		public string ClientSecret { get; set; }
		public string RedirectURI { get; set; }
	}
}

