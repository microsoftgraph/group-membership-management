// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.SecurityGroup
{
    public class GroupsReaderRequest
    {
        public Guid RunId { get; set; }
        public Guid ObjectId { get; set; }
    }
}