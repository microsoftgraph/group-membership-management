// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class JobTrackerEntity : IJobTracker
    {
        public JobState JobState { get; set; } = new JobState();

        public Task AddCompletedPart(string filePath)
        {
            if (!JobState.CompletedParts.Contains(filePath))
                JobState.CompletedParts.Add(filePath);

            return Task.CompletedTask;
        }

        public Task SetDestinationPart(string filePath)
        {
            JobState.DestinationPart = filePath;
            return Task.CompletedTask;
        }

        public Task<JobState> GetState()
        {
            return Task.FromResult(JobState);
        }

        public Task<bool> IsComplete()
        {
            var allPartsCompleted = JobState.TotalParts > 0
                                    && JobState.CompletedParts.Count == JobState.TotalParts;

            return Task.FromResult(allPartsCompleted);
        }

        public Task SetTotalParts(int totalParts)
        {
            JobState.TotalParts = totalParts;
            return Task.CompletedTask;
        }

        public virtual async Task Delete()
        {
            JobState = null;
            await Task.CompletedTask;
        }

        [Function(nameof(JobTrackerEntity))]
        public static Task RunAsync([EntityTrigger] TaskEntityDispatcher ctx)
        {
            return ctx.DispatchAsync<JobTrackerEntity>();
        }
    }
}
