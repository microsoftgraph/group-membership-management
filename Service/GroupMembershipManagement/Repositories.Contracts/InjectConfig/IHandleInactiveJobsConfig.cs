// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IHandleInactiveJobsConfig
    {
        public bool HandleInactiveJobsEnabled { get; }
        public int NumberOfDaysBeforePurging { get; }
        public int NumberOfDaysBeforePurgingToSendWarning { get; }
        public int NumberOfDaysBeforeDeletion { get; }
        public int JobHistoryRetentionDays { get; }
    }
}
