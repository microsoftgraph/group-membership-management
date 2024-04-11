// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Common.DependencyInjection
{
    public enum AuthenticationType
    {
        Unknown = 0,
        ClientSecret = 1,
        Certificate = 2,
        UserAssignedManagedIdentity = 3
    }
}
