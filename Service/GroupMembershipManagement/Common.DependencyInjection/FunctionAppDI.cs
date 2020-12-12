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
        public static IAuthenticationProvider CreateAuthProvider(GraphCredentials creds)
        {
            var confidentialClientApplication = ConfidentialClientApplicationBuilder
            .Create(creds.ClientId)
            .WithTenantId(creds.TenantId)
            .WithCertificate(GetCertificate(creds.CertificateName, creds.KeyVaultName, creds.KeyVaultTenantId))
            .Build();

            return new ClientCredentialProvider(confidentialClientApplication);
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
