// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;
using System.Diagnostics.CodeAnalysis;

namespace Tests.FunctionApps.Mocks
{
    public class MockDryRunValue : IDryRunValue
    {
        public bool DryRunEnabled { get; set; }

        public MockDryRunValue(bool dryRunEnabled)
        {
            this.DryRunEnabled = dryRunEnabled;
        }

        public MockDryRunValue()
        {

        }
    }
}
