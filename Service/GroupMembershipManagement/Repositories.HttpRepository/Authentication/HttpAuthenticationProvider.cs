// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using Services.Contracts.Http;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Repositories.HttpRepository.Authentication
{
    public class HttpAuthenticationProvider : IHttpAuthenticationProvider
    {
        private readonly IHttpRequestTokenProvider _authProvider;
        private readonly string _authority;
        private readonly string _resourceUri;
        private readonly string _userAgent;

        public HttpAuthenticationProvider(IHttpRequestTokenProvider authProvider, string authority, string resourceUri, string userAgent)
        {
            _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
            _authority = string.IsNullOrWhiteSpace(authority) ? throw new ArgumentNullException(nameof(authority)) : authority;
            _resourceUri = string.IsNullOrWhiteSpace(resourceUri) ? throw new ArgumentNullException(nameof(resourceUri)) : resourceUri;
            _userAgent = string.IsNullOrWhiteSpace(userAgent) ? throw new ArgumentNullException(nameof(userAgent)) : userAgent;
        }

        public async Task AuthenticateHttpClientAsync(HttpClient httpClient)
        {
            var token = await _authProvider.GetAccessTokenAsync(_authority, _resourceUri, "");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Add("User-Agent", _userAgent);
        }
    }
}
