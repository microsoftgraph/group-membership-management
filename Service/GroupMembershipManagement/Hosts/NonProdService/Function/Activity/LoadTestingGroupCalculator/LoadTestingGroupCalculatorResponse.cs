// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class LoadTestingGroupCalculatorResponse
    {
        public Dictionary<int,int> GroupSizesAndCounts { get; set; }
    }
}