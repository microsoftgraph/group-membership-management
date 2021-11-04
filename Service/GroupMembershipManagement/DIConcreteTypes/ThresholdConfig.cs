// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class ThresholdConfig : IThresholdConfig
    {
        public ThresholdConfig() {}

        public ThresholdConfig(
            int maximumNumberOfThresholdRecipients,
            int numberOfThresholdViolationsToNotify,
            int numberOfThresholdViolationsFollowUps,
            int numberOfThresholdViolationsToDisableJob)
        {
            MaximumNumberOfThresholdRecipients = maximumNumberOfThresholdRecipients;
            NumberOfThresholdViolationsToNotify = numberOfThresholdViolationsToNotify;
            NumberOfThresholdViolationsFollowUps = numberOfThresholdViolationsFollowUps;
            NumberOfThresholdViolationsToDisableJob = numberOfThresholdViolationsToDisableJob;
        }

        public int MaximumNumberOfThresholdRecipients { get; set; }
        public int NumberOfThresholdViolationsToNotify { get; set; }
        public int NumberOfThresholdViolationsFollowUps { get; set; }
        public int NumberOfThresholdViolationsToDisableJob { get; set; }
    }
}
