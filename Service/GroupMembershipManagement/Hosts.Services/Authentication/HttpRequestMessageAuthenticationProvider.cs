// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Polly;
using Services.Contracts;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading.Tasks;

namespace Services.Authentication
{
    [ExcludeFromCodeCoverage]
    public class HttpRequestMessageAuthenticationProvider : IHttpRequestAuthenticationProvider<HttpRequestMessage>, IAuthenticationProvider
    {        
        private readonly string _authority;
        private readonly string _resourceUri;
        private readonly ClientCredential _clientCredential;
        private readonly int MAX_TOKEN_RETRY_ATTEMPTS = 3;

        public HttpRequestMessageAuthenticationProvider(ClientCredential credential, string authority, string resourceUri)
        {
            _authority = string.IsNullOrWhiteSpace(authority)
                ? throw new ArgumentNullException(nameof(authority))
                : authority;
            _resourceUri = string.IsNullOrWhiteSpace(resourceUri)
                ? throw new ArgumentNullException(nameof(resourceUri))
                : resourceUri;
            _clientCredential = credential ?? throw new ArgumentNullException(nameof(credential));           
        }        

        public async Task AuthenticateRequestAsync(HttpRequestMessage request)
        {
            var token = await GetAccessTokenAsync(_authority, _resourceUri, "");
            request.Headers.Add("Authorization", $"Bearer {token}");            
        }

        [ExcludeFromCodeCoverage] // AAD Integration
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
