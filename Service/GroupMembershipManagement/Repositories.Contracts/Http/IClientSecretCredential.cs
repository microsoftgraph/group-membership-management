// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Contracts.Http
{
    public interface IClientSecretCredential
    {
        public string ClientId { get; }
        public string ClientSecret { get; }
    }
}
