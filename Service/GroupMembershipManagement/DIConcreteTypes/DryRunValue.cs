// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class DryRunValue : IDryRunValue
    {
        public string DryRunEnabled { get; set; }

        public DryRunValue(string dryRunEnabled)
        {
            this.DryRunEnabled = dryRunEnabled;
        }

        public DryRunValue()
        {

        }
    }
}
