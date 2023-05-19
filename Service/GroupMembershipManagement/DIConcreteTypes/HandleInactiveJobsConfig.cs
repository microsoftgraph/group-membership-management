// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class HandleInactiveJobsConfig : IHandleInactiveJobsConfig
    {
        public bool HandleInactiveJobsEnabled { get; set; }
        public int NumberOfDaysBeforeDeletion { get; set; }

        public HandleInactiveJobsConfig() {}

        public HandleInactiveJobsConfig(bool handleInactiveJobsEnabled, int numberOfDaysBeforeDeletion)
        {
            HandleInactiveJobsEnabled = handleInactiveJobsEnabled;
            NumberOfDaysBeforeDeletion = numberOfDaysBeforeDeletion;
        }
    }
}
