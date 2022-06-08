// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;

namespace Services.Contracts.Http
{
    public interface IHttpRequestTokenProvider
    {
        Task<string> GetAccessTokenAsync(string authority, string resource, string scope);
    }
}
