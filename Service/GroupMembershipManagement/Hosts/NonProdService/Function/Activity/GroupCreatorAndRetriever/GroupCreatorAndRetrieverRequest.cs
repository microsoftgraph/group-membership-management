// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class GroupCreatorAndRetrieverRequest
    {
        public string GroupName { get; set; }
        public TestGroupType TestGroupType { get; set; }
        public Guid RunId { get; set; }
    }
}