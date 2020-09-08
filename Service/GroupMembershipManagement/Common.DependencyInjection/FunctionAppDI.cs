// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using System;

namespace Common.DependencyInjection
{
	public static class FunctionAppDI
	{
		public static IAuthenticationProvider CreateAuthProvider(GraphCredentials creds)
		{
			var confidentialClientApplication = ConfidentialClientApplicationBuilder
			.Create(creds.ClientId)
			.WithTenantId(creds.TenantId)
			.WithClientSecret(creds.ClientSecret)
			.Build();

			return new ClientCredentialProvider(confidentialClientApplication);
		}

	}
}

