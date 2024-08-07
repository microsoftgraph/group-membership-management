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
        public static TokenCredential CreateAuthenticationProvider(GraphCredentials credentials, AuthenticationType authenticationType)
        {
            TokenCredential tokenCredential;
            switch (authenticationType)
            {
                default:
                case AuthenticationType.Unknown:
                    throw new Exception($"AuthenticationType is not valid, " +
                                        $"verify GraphAPI:AuthenticationType setting has been properly set in your App Configuration.");
                case AuthenticationType.ClientSecret:
                    tokenCredential = CreateAuthProviderFromSecret(credentials);
                    break;
                case AuthenticationType.Certificate:
                    tokenCredential = CreateAuthProviderFromCertificate(credentials);
                    break;
                case AuthenticationType.UserAssignedManagedIdentity:
                    tokenCredential = CreateUserAssignedManagedIdentityCredential(credentials.UserAssignedManagedIdentityClientId);
                    break;
            }

            return tokenCredential;
        }

        public static TokenCredential CreateAuthProviderFromSecret(GraphCredentials creds)
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

        public static TokenCredential CreateUserAssignedManagedIdentityCredential(string clientId)
        {
            return new ManagedIdentityCredential(clientId);
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
