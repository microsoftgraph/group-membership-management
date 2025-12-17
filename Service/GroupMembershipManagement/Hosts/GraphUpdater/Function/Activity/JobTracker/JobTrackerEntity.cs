// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;
using System.Threading.Tasks;

namespace GraphUpdater.Activity.JobTracker
{
    public class JobTrackerEntity : TaskEntity<JobState>, IJobTracker
    {
        protected override JobState InitializeState(TaskEntityOperation operation)
        {
            return new JobState();
        }

        public Task<JobState> GetState()
        {
            return Task.FromResult(State);
        }

        public Task SetState(JobState state)
        {
            State = state;
            return Task.CompletedTask;
        }

        [Function(nameof(JobTrackerEntity))]
        public static Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
        {
            return dispatcher.DispatchAsync<JobTrackerEntity>();
        }
    }
}
