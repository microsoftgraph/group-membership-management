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
        public static IAuthenticationProvider CreateAuthProviderFromCertificate(GraphCredentials creds)
        {
            var confidentialClientApplication = ConfidentialClientApplicationBuilder
            .Create(creds.ClientId)
            .WithTenantId(creds.TenantId)
            .WithCertificate(GetCertificate(creds.CertificateName, creds.KeyVaultName, creds.KeyVaultTenantId))
            .Build();

            return new ClientCredentialProvider(confidentialClientApplication);
        }

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

        private static X509Certificate2 GetCertificate(string certificateName, string keyVaultName, string keyVaultTenantId)
        {
            var options = new DefaultAzureCredentialOptions();
#if DEBUG
            options.VisualStudioTenantId = keyVaultTenantId;
#endif

            var defaultCredential = new DefaultAzureCredential(options);
            var keyVaultBaseUrl = new Uri($"https://{keyVaultName}.vault.azure.net/");
            var secretClient = new SecretClient(keyVaultBaseUrl, defaultCredential);
            var privateKey = secretClient.GetSecret(certificateName);
            var privateKeyDecoded = Convert.FromBase64String(privateKey.Value.Value);
            var certificate = new X509Certificate2(privateKeyDecoded, (string)null, X509KeyStorageFlags.MachineKeySet);
            return certificate;
        }
    }
}
