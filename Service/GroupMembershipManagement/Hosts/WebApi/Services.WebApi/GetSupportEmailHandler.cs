// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class GetSupportEmailHandler : RequestHandlerBase<GetSupportEmailRequest, GetSupportEmailResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly SecretClient _keyVaultClient;

        public GetSupportEmailHandler(ILoggingRepository loggingRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _keyVaultClient = CreateSecretClient();
        }

        protected virtual SecretClient CreateSecretClient()
        {
            var keyVaultUri = "https://prereqs.vault.azure.net/";
            return new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
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
                KeyVaultSecret secret = await _keyVaultClient.GetSecretAsync(secretName);
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