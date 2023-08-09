// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.PlaceMembershipObtainer
{
    public class UsersReaderRequest
    {
        public Guid RunId { get; set; }
        public string Url { get; set; }
    }
}