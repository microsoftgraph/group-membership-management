// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Net.Http;
using System.Threading.Tasks;

namespace Services.Contracts.Http
{
    public interface IHttpAuthenticationProvider
    {
        Task AuthenticateHttpClientAsync(HttpClient httpClient);
    }
}