// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System;
using System.Threading.Tasks;
using WebApi.Models;

namespace Services
{
    public class GetSupportEmailHandler : RequestHandlerBase<GetSupportEmailRequest, GetSupportEmailResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly SecretClient _keyVaultClient;
        private readonly IOptions<WebApiSettings> _webApiSettings;

        public GetSupportEmailHandler(ILoggingRepository loggingRepository, IOptions<WebApiSettings> webApiSettings) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            if (webApiSettings?.Value == null)
                {
                    throw new ArgumentNullException(nameof(webApiSettings));
                }
            _keyVaultClient = CreateSecretClient(webApiSettings.Value.KeyVaultName);
        }

        protected virtual SecretClient CreateSecretClient(string keyVaultName)
        {   
            if (string.IsNullOrEmpty(keyVaultName))
            {
                throw new ArgumentException("KeyVaultName cannot be null or empty", nameof(keyVaultName));
            }
            
            var keyVaultUri = $"https://{keyVaultName}.vault.azure.net/";
            TokenCredential credential;
#if DEBUG
            credential = new DefaultAzureCredential();
#else
            credential = new ManagedIdentityCredential();
#endif

            return new SecretClient(new Uri(keyVaultUri), credential);
        }

        protected override async Task<GetSupportEmailResponse> ExecuteCoreAsync(GetSupportEmailRequest request)
        {
            try
            {
                var supportEmailAddress = await GetSecretValueAsync("supportEmailAddresses");

                if (string.IsNullOrWhiteSpace(supportEmailAddress))
                {
                    supportEmailAddress = await GetSecretValueAsync("senderAddress");
                }

                var response = new GetSupportEmailResponse
                {
                    SupportEmailAddress = supportEmailAddress
                };

                return response;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unable to retrieve support email addresses: {ex.Message}" });
                throw;
            }
        }

        private async Task<string> GetSecretValueAsync(string secretName)
        {
            try
            {
                KeyVaultSecret secret = await _keyVaultClient.GetSecretAsync(secretName, cancellationToken: CancellationToken.None);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Retrieved secret '{secretName}' successfully." });
                return secret.Value;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Failed to retrieve secret '{secretName}' from Key Vault: {ex.Message}" });
                return null;
            }
        }
    }
}