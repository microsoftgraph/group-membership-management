// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly ILogger<GetSupportEmailHandler> _logger;
        private readonly SecretClient _keyVaultClient;
        private readonly IOptions<WebApiSettings> _webApiSettings;

        public GetSupportEmailHandler(ILogger<GetSupportEmailHandler> logger, IOptions<WebApiSettings> webApiSettings) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
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
                _logger.SupportEmailRetrievalFailed(ex);
                throw;
            }
        }

        private async Task<string?> GetSecretValueAsync(string secretName)
        {
            try
            {
                KeyVaultSecret secret = await _keyVaultClient.GetSecretAsync(secretName, cancellationToken: CancellationToken.None);
                _logger.SecretRetrievedFromKeyVault(secretName);
                return secret.Value;
            }
            catch (Exception ex)
            {
                _logger.SecretRetrievalFailedFromKeyVault(secretName, ex);
                return null;
            }
        }
    }
}