// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using Polly;
using System.Net.Http;
using System.Net;
using Newtonsoft.Json;
using Services.Contracts.Http;
using Repositories.Contracts;

namespace Repositories.HttpRepository
{
    public class HttpRepository: IHttpRepository
    {
        private readonly HttpClient _httpClient = null;
        private readonly IHttpAuthenticationProvider _authProvider = null;

        private readonly int MAX_HTTP_RETRY_ATTEMPTS = 3;

        public HttpRepository(IHttpClientFactory httpClientFactory, IHttpAuthenticationProvider authProvider)
        {
            _httpClient = httpClientFactory.CreateClient();
            _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
        }
        public async Task<T> Fetch<T>(string url)
        {
            await _authProvider.AuthenticateHttpClientAsync(_httpClient);

            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(
                    MAX_HTTP_RETRY_ATTEMPTS,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                );

            var json = "{}";
            await retryPolicy.ExecuteAsync(async () =>
            {
                using (var response = await _httpClient.GetAsync(url))
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"Response StatusCode was {response.StatusCode}");
                    json = await response.Content.ReadAsStringAsync();
                }
            });

            return JsonConvert.DeserializeObject<T>(json);
        }

    }
}
