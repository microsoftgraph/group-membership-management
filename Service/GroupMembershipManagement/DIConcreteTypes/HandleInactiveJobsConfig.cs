// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class HandleInactiveJobsConfig : IHandleInactiveJobsConfig
    {
        public bool HandleInactiveJobsEnabled { get; set; }
        public int NumberOfDaysBeforePurging { get; set; }
        public int NumberOfDaysBeforePurgingToSendWarning { get; set; }
        public int NumberOfDaysBeforeDeletion { get; set; }

        public HandleInactiveJobsConfig() {}

        public HandleInactiveJobsConfig(bool handleInactiveJobsEnabled, int numberOfDaysBeforePurging, int numberOfDaysBeforePurgingToSendWarning, int numberOfDaysBeforeDeletion)
        {
            HandleInactiveJobsEnabled = handleInactiveJobsEnabled;
            NumberOfDaysBeforePurging = numberOfDaysBeforePurging;
            NumberOfDaysBeforePurgingToSendWarning = numberOfDaysBeforePurgingToSendWarning;
            NumberOfDaysBeforeDeletion = numberOfDaysBeforeDeletion;
        }
    }
}
