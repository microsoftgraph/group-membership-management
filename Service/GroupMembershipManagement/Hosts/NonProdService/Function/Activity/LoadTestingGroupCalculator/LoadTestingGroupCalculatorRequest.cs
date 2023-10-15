// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.NonProdService
{
    public class LoadTestingGroupCalculatorRequest
    {
        public int NumberOfGroups { get; set; }
        public int NumberOfUsers { get; set; }
        public Guid RunId { get; set; }
    }
}