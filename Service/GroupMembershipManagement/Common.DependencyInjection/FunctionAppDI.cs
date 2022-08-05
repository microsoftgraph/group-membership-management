// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using System;
using System.Security.Cryptography.X509Certificates;

namespace Common.DependencyInjection
{
    public static class FunctionAppDI
    {
        public static IAuthenticationProvider CreateAuthProviderFromSecret(GraphCredentials creds)
        {
            var confidentialClientApplication = ConfidentialClientApplicationBuilder
            .Create(creds.ClientId)
            .WithTenantId(creds.TenantId)
            .WithClientSecret(creds.ClientSecret)
            .Build();

            return new ClientCredentialProvider(confidentialClientApplication);
        }

        public static IAuthenticationProvider CreateMailAuthProvider(GraphCredentials creds)
        {
            var publicClientApplication = PublicClientApplicationBuilder
            .Create(creds.ClientId)
            .WithTenantId(creds.TenantId)
            .Build();

            return new UsernamePasswordProvider(publicClientApplication);
        }
    }
}
