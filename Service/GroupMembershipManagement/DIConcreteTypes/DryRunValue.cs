// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class DryRunValue : IDryRunValue
    {
        public bool DryRunEnabled { get; set; }

        public DryRunValue(bool dryRunEnabled)
        {
            this.DryRunEnabled = dryRunEnabled;
        }

        public DryRunValue()
        {

        }
    }
}
