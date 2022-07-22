// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;

namespace Hosts.SecurityGroup
{
    public class DeltaUsersReaderRequest
    {
        public Guid RunId { get; set; }
        public string DeltaLink { get; set; }        
    }
}