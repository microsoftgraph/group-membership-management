// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Security.Cryptography.X509Certificates;

namespace Common.DependencyInjection
{
    public static class FunctionAppDI
    {
        public static TokenCredential CreateAuthenticationProvider(GraphCredentials credentials)
        {
            if (!string.IsNullOrWhiteSpace(credentials.ClientCertificateName)
                && credentials.ClientCertificateName != "not-set")
            {
                return CreateAuthProviderFromCertificate(credentials);
            }

            return CreateAuthProviderFromSecret(credentials);
        }

        private static TokenCredential CreateAuthProviderFromSecret(GraphCredentials creds)
        {
            return new ClientSecretCredential(creds.TenantId, creds.ClientId, creds.ClientSecret);
        }

        private static TokenCredential CreateAuthProviderFromCertificate(GraphCredentials creds)
        {
            return new ClientCertificateCredential(creds.TenantId, creds.ClientId, GetCertificate(creds.ClientCertificateName, creds.KeyVaultName));
        }

        public static TokenCredential CreateServiceAccountAuthProvider(GraphCredentials creds)
        {
            return new UsernamePasswordCredential(creds.ServiceAccountUserName, creds.ServiceAccountPassword, creds.TenantId, creds.ClientId);
        }

        private static X509Certificate2 GetCertificate(string certificateName, string keyVaultName)
        {
            var options = new DefaultAzureCredentialOptions();
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
