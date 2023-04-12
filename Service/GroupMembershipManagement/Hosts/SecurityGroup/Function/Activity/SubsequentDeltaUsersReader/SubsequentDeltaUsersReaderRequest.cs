// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.SecurityGroup
{
    public class SubsequentDeltaUsersReaderRequest
    {
        public Guid RunId { get; set; }
        public string NextPageUrl { get; set; }
    }
}