// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    /// <summary>
    /// The kind of Microsoft Entra group GMM creates on behalf of a user.
    /// The default is <see cref="Microsoft365"/>, so a request that omits the type
    /// keeps the behaviour that existed before security groups were supported.
    /// </summary>
    public enum NewGroupType
    {
        Microsoft365 = 0,
        Security = 1
    }
}
