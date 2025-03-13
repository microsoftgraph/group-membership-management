// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;

namespace GraphUpdater.Activity.JobTracker
{
    public interface IJobTracker
    {
        Task<JobState> GetState();
        Task SetState(JobState state);
    }
}
