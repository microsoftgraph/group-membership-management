// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Contracts.Http;
using System;

namespace DIConcreteTypes
{
    public class ClientSecretCredential : IClientSecretCredential
    {
        public string ClientId { get; }
        public string ClientSecret { get; }

        public ClientSecretCredential(string clientId, string clientSecret)
        {
            ClientId = string.IsNullOrWhiteSpace(clientId)
                ? throw new ArgumentNullException(nameof(clientId))
                : clientId;
            ClientSecret = string.IsNullOrWhiteSpace(clientSecret)
                ? throw new ArgumentNullException(nameof(clientSecret))
                : clientSecret;
        }
    }
}
