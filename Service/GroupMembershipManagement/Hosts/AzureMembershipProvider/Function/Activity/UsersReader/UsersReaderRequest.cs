// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.AzureMembershipProvider
{
    public class UsersReaderRequest
    {
        public Guid RunId { get; set; }
        public string Url { get; set; }
    }
}