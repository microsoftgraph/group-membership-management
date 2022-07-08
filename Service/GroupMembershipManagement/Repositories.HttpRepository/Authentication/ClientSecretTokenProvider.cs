// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Polly;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Services.Contracts.Http;

namespace Repositories.HttpRepository.Authentication
{
    public class ClientSecretTokenProvider : IHttpRequestTokenProvider
    {
        private readonly ClientCredential _clientCredential;
        private readonly int MAX_TOKEN_RETRY_ATTEMPTS = 3;

        public ClientSecretTokenProvider(IClientSecretCredential credential)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (string.IsNullOrWhiteSpace(credential.ClientId)) throw new ArgumentNullException(nameof(credential.ClientId));
            if (string.IsNullOrWhiteSpace(credential.ClientSecret)) throw new ArgumentNullException(nameof(credential.ClientSecret));

            _clientCredential = new ClientCredential(credential.ClientId, credential.ClientSecret);
        }

        public async Task<string> GetAccessTokenAsync(string authority, string resource, string scope)
        {
            var authenticationContext = new AuthenticationContext(authority, false);
            var retryPolicy = Policy
                .Handle<AdalServiceException>()
                .WaitAndRetryAsync(
                    MAX_TOKEN_RETRY_ATTEMPTS,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                );

            AuthenticationResult result = null;
            await retryPolicy.ExecuteAsync(async () =>
            {
                result = await authenticationContext.AcquireTokenAsync(resource, _clientCredential);
            });
            return result.AccessToken;
        }
    }
}
