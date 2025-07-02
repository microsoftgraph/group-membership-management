// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class LoadTestingGroupCalculatorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;
        private readonly List<int> _groupSizes = new List<int> { 10, 50, 100, 500, 1000, 5000, 10000, 50000, 100000, 200000, 250000 };

        public LoadTestingGroupCalculatorFunction(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        /// <summary>
        /// Creates a dictionary of group sizes and the number of groups of that size to create.
        /// It attempts to create many more smaller groups than larger groups to more closely resemble production usage.
        /// </summary>
        [FunctionName(nameof(LoadTestingGroupCalculatorFunction))]
        public async Task<LoadTestingGroupCalculatorResponse> GenerateGroup([ActivityTrigger] LoadTestingGroupCalculatorRequest request, ILogger log)
        {
            var runId = request.RunId;
            var numberOfUsers = request.NumberOfUsers;
            var numberOfGroups = request.NumberOfGroups;
            var existingGroupNames = request.ExistingGroupNames;

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(LoadTestingGroupCalculatorFunction)} function started",
                RunId = runId
            }, VerbosityLevel.DEBUG);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Number of existing Load Test groups found: {existingGroupNames.Count(name =>
                    name.StartsWith("LoadTesting_DestinationGroup_", StringComparison.OrdinalIgnoreCase))}",
                RunId = runId
            }, VerbosityLevel.DEBUG);

            // Step 1: Count existing groups by group size
            var existingGroupCounts = new Dictionary<int, int>();
            foreach (var size in _groupSizes)
            {
                var prefix = $"LoadTesting_DestinationGroup_{size}_";
                existingGroupCounts[size] = existingGroupNames.Count(name =>
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }

            // Step 2: Find largest group size eligible for current number of users
            var maxGroupSize = _groupSizes.LastOrDefault(size => size < numberOfUsers);
            var maxGroupSizeIndex = _groupSizes.IndexOf(maxGroupSize);

            // Step 3: Build Fibonacci-weighted distribution
            var sequence = DistinctFibonacciSequence(maxGroupSizeIndex + 1);
            var sum = sequence.Sum();

            var fullTargetDistribution = new Dictionary<int, int>();
            var totalCountSoFar = 0;

            for (int i = 0; maxGroupSizeIndex - i >= 0; i++)
            {
                var groupSize = _groupSizes[maxGroupSizeIndex - i];
                var count = (int)(((decimal)sequence[i] / sum) * numberOfGroups);

                if (count > 0)
                {
                    fullTargetDistribution[groupSize] = count;
                    totalCountSoFar += count;
                }
            }

            // Optional: fix for rounding drift
            var leftover = numberOfGroups - totalCountSoFar;
            if (leftover > 0)
            {
                var smallestSize = _groupSizes[0];
                if (fullTargetDistribution.ContainsKey(smallestSize))
                    fullTargetDistribution[smallestSize] += leftover;
                else
                    fullTargetDistribution[smallestSize] = leftover;
            }

            var distributionLog = string.Join(", ", fullTargetDistribution.OrderBy(kvp => kvp.Key).Select(kvp => $"[{kvp.Key}]={kvp.Value}"));
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Target Distribution: {distributionLog}",
                RunId = runId
            }, VerbosityLevel.DEBUG);

            // Step 4: Calculate missing groups per size
            var groupsToCreate = new Dictionary<int, int>();
            foreach (var kvp in fullTargetDistribution)
            {
                var size = kvp.Key;
                var targetCount = kvp.Value;
                var currentCount = existingGroupCounts.GetValueOrDefault(size, 0);
                var missingCount = targetCount - currentCount;

                if (missingCount > 0)
                    groupsToCreate[size] = missingCount;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Existing Groups: {string.Join(", ", existingGroupCounts.OrderBy(kvp => kvp.Key).Select(kvp => $"[{kvp.Key}]={existingGroupCounts.GetValueOrDefault(kvp.Key, 0)}"))}",
                RunId = runId
            }, VerbosityLevel.DEBUG);

            var groupsTobeCreatedMessage = $"Groups to be Created : {string.Join(", ", groupsToCreate.OrderBy(kvp => kvp.Key).Select(kvp => $"[{kvp.Key}]={kvp.Value}"))}";
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = groupsToCreate.Count > 0 ? groupsTobeCreatedMessage : "No Groups Need to be Created.",
                RunId = runId
            }, VerbosityLevel.DEBUG);

            return new LoadTestingGroupCalculatorResponse
            {
                GroupSizesAndCounts = groupsToCreate,
                ExpectedTargetDistribution = fullTargetDistribution

            };
        }

        public List<int> DistinctFibonacciSequence(int sequenceLength)
        {
            List<int> sequence = new List<int>();
            // First two numbers in the sequence are always 1, so by setting b to 2, we can skip the first iteration.
            int a = 1;
            int b = 2;

            while (sequence.Count < sequenceLength)
            {
                sequence.Add(a);
                int c = a + b;
                a = b;
                b = c;
            }

            return sequence;
        }
    }
}
