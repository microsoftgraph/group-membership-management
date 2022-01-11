// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IThresholdConfig
    {
        public int MaximumNumberOfThresholdRecipients { get; }
        public int NumberOfThresholdViolationsToNotify { get; }
        public int NumberOfThresholdViolationsFollowUps { get; }
        public int NumberOfThresholdViolationsToDisableJob { get; }
    }
}
