// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class JobTrackerEntity : TaskEntity<JobState>, IJobTracker
    {
        public Task AddCompletedPart(string filePath)
        {
            if (!State.CompletedParts.Contains(filePath))
                State.CompletedParts.Add(filePath);

            return Task.CompletedTask;
        }

        public Task SetDestinationPart(string filePath)
        {
            State.DestinationPart = filePath;
            return Task.CompletedTask;
        }

        public Task<JobState> GetState()
        {
            return Task.FromResult(State);
        }

        public Task<bool> IsComplete()
        {
            var allPartsCompleted = State.TotalParts > 0
                                    && State.CompletedParts.Count == State.TotalParts;

            return Task.FromResult(allPartsCompleted);
        }

        public Task SetTotalParts(int totalParts)
        {
            State.TotalParts = totalParts;
            return Task.CompletedTask;
        }

        [Function(nameof(JobTrackerEntity))]
        public static Task Run([EntityTrigger] TaskEntityDispatcher ctx)
        {
            return ctx.DispatchAsync<JobTrackerEntity>();
        }

        protected override JobState InitializeState(TaskEntityOperation entityOperation)
        {
            return new JobState();
        }
    }
}
