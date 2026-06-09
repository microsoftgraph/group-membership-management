// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class GetAllGroupNamesResponse
    {
        public Dictionary<Guid, string> Groups { get; set; }
    }
}