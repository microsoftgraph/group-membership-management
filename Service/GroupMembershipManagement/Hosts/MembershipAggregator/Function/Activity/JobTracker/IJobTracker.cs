// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public interface IJobTracker
    {
        Task AddCompletedPart(string filePath);
        Task<JobState> GetState();
        Task<bool> IsComplete();
        Task SetTotalParts(int totalParts);
        Task Delete();
    }
}
