// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class ThresholdConfig : IThresholdConfig
    {
        public ThresholdConfig() {}

        public ThresholdConfig(int maximumNumberOfThresholdRecipients)
        {
            MaximumNumberOfThresholdRecipients = maximumNumberOfThresholdRecipients;
        }

        public int MaximumNumberOfThresholdRecipients { get; set; }
    }
}
