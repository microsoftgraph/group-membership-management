// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.SecurityGroup
{
    public class GetTransitiveGroupCountRequest
    {
        public Guid RunId { get; set; }
        public Guid GroupId { get; set; }
    }
}