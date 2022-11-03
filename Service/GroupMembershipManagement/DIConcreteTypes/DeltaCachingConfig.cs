// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class DeltaCachingConfig : IDeltaCachingConfig
    {
        public bool DeltaCacheEnabled { get; set; }
        public DeltaCachingConfig() {}

        public DeltaCachingConfig(bool deltaCacheEnabled)
        {
            DeltaCacheEnabled = deltaCacheEnabled;
        }
    }
}
