// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace Repositories.Mocks
{
    public class MockDeltaCachingConfig : IDeltaCachingConfig
    {
        public bool DeltaCacheEnabled { get; set; }

        public MockDeltaCachingConfig()
        {
            DeltaCacheEnabled = true;
        }
    }
}