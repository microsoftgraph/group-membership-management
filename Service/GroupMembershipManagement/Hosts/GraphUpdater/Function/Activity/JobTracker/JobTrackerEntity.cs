// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Threading.Tasks;

namespace GraphUpdater.Activity.JobTracker
{
    public class JobTrackerEntity : IJobTracker
    {
        public JobState JobState { get; set; } = new JobState();

        public Task<JobState> GetState()
        {
            return Task.FromResult(JobState);
        }

        public Task SetState(JobState state)
        {
            JobState = state;
            return Task.CompletedTask;
        }

        [FunctionName(nameof(JobTrackerEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx)
        {
            return ctx.DispatchAsync<JobTrackerEntity>();
        }
    }
}
